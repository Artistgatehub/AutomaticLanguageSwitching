using System.Text.Json;

namespace AutomaticLanguageSwitching.NativeHost;

internal sealed class NativeMessagingHost
{
    private const int ProtocolVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly Dictionary<TabKey, string> _rememberedLayouts = new();
    private readonly KeyboardLayoutService _keyboardLayoutService = new();
    private readonly PerAppInputMethodStatus _perAppInputMethodStatus;
    private TabKey? _currentActiveTab;

    public NativeMessagingHost(Stream input, Stream output)
    {
        _input = input;
        _output = output;
        _perAppInputMethodStatus = EnsurePerAppInputMethodSetting();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Console.Error.WriteLine("[als-host] Native Messaging loop started.");
        while (!cancellationToken.IsCancellationRequested)
        {
            var incoming = await ReadMessageAsync(cancellationToken);
            if (incoming is null)
            {
                Console.Error.WriteLine("[als-host] Native Messaging input closed.");
                return;
            }

            await HandleMessageAsync(incoming, cancellationToken);
        }
    }

    private async Task HandleMessageAsync(HostMessage message, CancellationToken cancellationToken)
    {
        if (message.Version != ProtocolVersion)
        {
            await SendErrorAsync($"Unsupported protocol version '{message.Version}'.", cancellationToken);
            return;
        }

        switch (message.Type)
        {
            case "hello":
                Console.Error.WriteLine($"[als-host] Hello from extension version={message.Payload.ExtensionVersion ?? "unknown"}.");
                await SendAsync(new HostMessage
                {
                    Version = ProtocolVersion,
                    Type = "hello_ack",
                    Payload = new MessagePayload
                    {
                        HostVersion = "0.2.0",
                        Platform = "windows",
                        PerAppInputMethodEnabled = _perAppInputMethodStatus.IsEnabled,
                        AttemptedAutoEnable = _perAppInputMethodStatus.AttemptedAutoEnable
                    }
                }, cancellationToken);
                Console.Error.WriteLine(
                    $"[als-host] hello_ack sent: settingEnabled={_perAppInputMethodStatus.IsEnabled} autoEnableAttempted={_perAppInputMethodStatus.AttemptedAutoEnable}.");

                if (!_perAppInputMethodStatus.IsEnabled)
                {
                    Console.Error.WriteLine("[als-host] Warning sent: Windows per-app input setting still disabled.");
                    await SendAsync(new HostMessage
                    {
                        Version = ProtocolVersion,
                        Type = "warning",
                        Payload = new MessagePayload
                        {
                            Message = "Windows per-app input method setting is disabled and could not be enabled automatically.",
                            PerAppInputMethodEnabled = false,
                            AttemptedAutoEnable = _perAppInputMethodStatus.AttemptedAutoEnable
                        }
                    }, cancellationToken);
                }
                break;
            case "tab_switched":
                await HandleTabSwitchedAsync(message, cancellationToken);
                break;
            case "chrome_focus_returned":
                await HandleChromeFocusReturnedAsync(message, cancellationToken);
                break;
            case "tab_closed":
                HandleTabClosed(message);
                break;
            default:
                await SendErrorAsync("Unsupported message type.", cancellationToken);
                break;
        }
    }

    private PerAppInputMethodStatus EnsurePerAppInputMethodSetting()
    {
        var initialState = _keyboardLayoutService.IsPerAppInputMethodEnabled();
        if (initialState is true)
        {
            Console.Error.WriteLine("[als-host] Startup check: Windows per-app input setting already enabled.");
            return new PerAppInputMethodStatus(true, false);
        }

        Console.Error.WriteLine("[als-host] Startup check: Windows per-app input setting disabled or unreadable; trying auto-enable.");

        var attemptedAutoEnable = _keyboardLayoutService.TryEnablePerAppInputMethod();
        var finalState = _keyboardLayoutService.IsPerAppInputMethodEnabled();

        if (finalState is true)
        {
            Console.Error.WriteLine(
                $"[als-host] Startup check: Windows per-app input setting enabled after auto-heal. attempted={attemptedAutoEnable}.");
            return new PerAppInputMethodStatus(true, attemptedAutoEnable);
        }

        Console.Error.WriteLine(
            $"[als-host] Startup check: Windows per-app input setting still disabled. attempted={attemptedAutoEnable}.");
        return new PerAppInputMethodStatus(false, attemptedAutoEnable);
    }

    private async Task HandleTabSwitchedAsync(HostMessage message, CancellationToken cancellationToken)
    {
        if (message.Payload.CurrentWindowId is null || message.Payload.CurrentTabId is null)
        {
            await SendErrorAsync("tab_switched requires currentWindowId and currentTabId.", cancellationToken);
            return;
        }

        var currentKey = new TabKey(
            message.Payload.CurrentWindowId.Value,
            message.Payload.CurrentTabId.Value);
        TabKey? reportedPreviousKey = null;

        if (message.Payload.PreviousWindowId is not null && message.Payload.PreviousTabId is not null)
        {
            reportedPreviousKey = new TabKey(
                message.Payload.PreviousWindowId.Value,
                message.Payload.PreviousTabId.Value);
        }

        var currentLayoutId = _keyboardLayoutService.GetCurrentLayoutId();

        if (reportedPreviousKey is not null &&
            _currentActiveTab is not null &&
            _currentActiveTab.Value != reportedPreviousKey.Value)
        {
            Console.Error.WriteLine(
                $"[als-host] Tab switch: reported previous={FormatTab(reportedPreviousKey)} mismatched tracked={FormatTab(_currentActiveTab)}; using tracked tab.");
        }

        var tabToRemember = _currentActiveTab ?? reportedPreviousKey;
        Console.Error.WriteLine(
            $"[als-host] Tab switch: previous={FormatTab(tabToRemember)} current={FormatTab(currentKey)}.");

        if (tabToRemember is not null &&
            tabToRemember.Value != currentKey &&
            !string.IsNullOrWhiteSpace(currentLayoutId))
        {
            _rememberedLayouts[tabToRemember.Value] = currentLayoutId;
            Console.Error.WriteLine(
                $"[als-host] Remember: tab={FormatTab(tabToRemember)} layout={currentLayoutId}.");
        }
        else if (tabToRemember is not null && tabToRemember.Value == currentKey)
        {
            Console.Error.WriteLine(
                $"[als-host] Remember ignored: previous tab {FormatTab(tabToRemember)} is the same as current.");
        }
        else if (tabToRemember is not null)
        {
            Console.Error.WriteLine(
                $"[als-host] Remember ignored: no current layout available for previous tab {FormatTab(tabToRemember)}.");
        }

        if (!_rememberedLayouts.TryGetValue(currentKey, out var layoutId))
        {
            Console.Error.WriteLine($"[als-host] Restore skipped: no remembered layout for {FormatTab(currentKey)}.");
            _currentActiveTab = currentKey;
            return;
        }

        await RestoreRememberedLayoutAsync(currentKey, layoutId, currentLayoutId, cancellationToken);
        _currentActiveTab = currentKey;
    }

    private async Task HandleChromeFocusReturnedAsync(HostMessage message, CancellationToken cancellationToken)
    {
        if (_currentActiveTab is null)
        {
            Console.Error.WriteLine("[als-host] Focus return ignored: no active tab is tracked.");
            return;
        }

        if (message.Payload.CurrentWindowId is not null &&
            message.Payload.CurrentTabId is not null)
        {
            var reportedCurrentKey = new TabKey(
                message.Payload.CurrentWindowId.Value,
                message.Payload.CurrentTabId.Value);

            if (reportedCurrentKey != _currentActiveTab.Value)
            {
                Console.Error.WriteLine(
                    $"[als-host] Focus return ignored: reported={FormatTab(reportedCurrentKey)} tracked={FormatTab(_currentActiveTab)}.");
                return;
            }
        }

        var currentKey = _currentActiveTab.Value;
        Console.Error.WriteLine($"[als-host] Focus return: tab={FormatTab(currentKey)}.");
        var currentLayoutId = _keyboardLayoutService.GetCurrentLayoutId();

        if (!_rememberedLayouts.TryGetValue(currentKey, out var layoutId))
        {
            Console.Error.WriteLine($"[als-host] Focus return restore skipped: no remembered layout for {FormatTab(currentKey)}.");
            return;
        }

        await RestoreRememberedLayoutAsync(currentKey, layoutId, currentLayoutId, cancellationToken);
    }

    private async Task RestoreRememberedLayoutAsync(
        TabKey currentKey,
        string layoutId,
        string? currentLayoutId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(layoutId, currentLayoutId, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"[als-host] Restore skipped: tab={FormatTab(currentKey)} already has layout={layoutId}.");

            await SendAsync(new HostMessage
            {
                Version = ProtocolVersion,
                Type = "layout_restore_result",
                Payload = new MessagePayload
                {
                    WindowId = currentKey.WindowId,
                    TabId = currentKey.TabId,
                    LayoutId = layoutId,
                    Result = "applied"
                }
            }, cancellationToken);
            return;
        }

        Console.Error.WriteLine($"[als-host] Restore: tab={FormatTab(currentKey)} layout={layoutId}.");
        var result = _keyboardLayoutService.TrySwitchTo(layoutId);
        Console.Error.WriteLine(
            $"[als-host] Restore result: tab={FormatTab(currentKey)} layout={layoutId} result={result}.");

        await SendAsync(new HostMessage
        {
            Version = ProtocolVersion,
            Type = "layout_restore_result",
            Payload = new MessagePayload
            {
                WindowId = currentKey.WindowId,
                TabId = currentKey.TabId,
                LayoutId = layoutId,
                Result = result switch
                {
                    LayoutSwitchResult.Applied => "applied",
                    LayoutSwitchResult.Unavailable => "unavailable",
                    _ => "failed"
                }
            }
        }, cancellationToken);
    }

    private void HandleTabClosed(HostMessage message)
    {
        if (message.Payload.WindowId is null || message.Payload.TabId is null)
        {
            Console.Error.WriteLine("[als-host] Tab close ignored: missing windowId/tabId.");
            return;
        }

        var key = new TabKey(message.Payload.WindowId.Value, message.Payload.TabId.Value);
        _rememberedLayouts.Remove(key);
        Console.Error.WriteLine($"[als-host] Tab closed: cleared remembered layout for {FormatTab(key)}.");

        if (_currentActiveTab is not null && _currentActiveTab.Value == key)
        {
            _currentActiveTab = null;
            Console.Error.WriteLine($"[als-host] Active tab cleared because {FormatTab(key)} was closed.");
        }
    }

    private static string FormatTab(TabKey? key)
    {
        return key is null ? "null" : $"{key.Value.WindowId}:{key.Value.TabId}";
    }

    private async Task<HostMessage?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[4];
        var headerBytesRead = await ReadExactlyAsync(_input, lengthBuffer, 4, cancellationToken);
        if (headerBytesRead == 0)
        {
            return null;
        }

        var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
        var payloadBuffer = new byte[messageLength];
        await ReadExactlyAsync(_input, payloadBuffer, messageLength, cancellationToken);

        return JsonSerializer.Deserialize<HostMessage>(payloadBuffer, JsonOptions)
            ?? throw new InvalidOperationException("Received an empty or invalid JSON message.");
    }

    private async Task SendAsync(HostMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var length = BitConverter.GetBytes(payload.Length);

        await _output.WriteAsync(length.AsMemory(0, length.Length), cancellationToken);
        await _output.WriteAsync(payload.AsMemory(0, payload.Length), cancellationToken);
        await _output.FlushAsync(cancellationToken);
    }

    private Task SendErrorAsync(string text, CancellationToken cancellationToken)
    {
        Console.Error.WriteLine($"[als-host] Error: {text}");
        return SendAsync(new HostMessage
        {
            Version = ProtocolVersion,
            Type = "error",
            Payload = new MessagePayload
            {
                Message = text
            }
        }, cancellationToken);
    }

    private static async Task<int> ReadExactlyAsync(
        Stream stream,
        byte[] buffer,
        int count,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(totalRead, count - totalRead),
                cancellationToken);

            if (bytesRead == 0)
            {
                if (totalRead == 0)
                {
                    return 0;
                }

                throw new InvalidOperationException("Unexpected end of stream while reading Native Messaging data.");
            }

            totalRead += bytesRead;
        }

        return totalRead;
    }
}

internal readonly record struct PerAppInputMethodStatus(
    bool IsEnabled,
    bool AttemptedAutoEnable);
