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
        while (!cancellationToken.IsCancellationRequested)
        {
            var incoming = await ReadMessageAsync(cancellationToken);
            if (incoming is null)
            {
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
                Console.Error.WriteLine($"[als-host] Extension connected. Version={message.Payload.ExtensionVersion ?? "unknown"}.");
                await SendAsync(new HostMessage
                {
                    Version = ProtocolVersion,
                    Type = "hello_ack",
                    Payload = new MessagePayload
                    {
                        HostVersion = "0.1.0",
                        Platform = "windows",
                        PerAppInputMethodEnabled = _perAppInputMethodStatus.IsEnabled,
                        AttemptedAutoEnable = _perAppInputMethodStatus.AttemptedAutoEnable
                    }
                }, cancellationToken);

                if (!_perAppInputMethodStatus.IsEnabled)
                {
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
            return new PerAppInputMethodStatus(true, false);
        }

        Console.Error.WriteLine(
            "[als-host] Per-app input method setting is disabled or unreadable. Attempting best-effort enable.");

        var attemptedAutoEnable = _keyboardLayoutService.TryEnablePerAppInputMethod();
        var finalState = _keyboardLayoutService.IsPerAppInputMethodEnabled();

        if (finalState is true)
        {
            Console.Error.WriteLine("[als-host] Per-app input method setting is enabled after startup check.");
            return new PerAppInputMethodStatus(true, attemptedAutoEnable);
        }

        Console.Error.WriteLine(
            "[als-host] Per-app input method setting remains disabled after startup check.");
        return new PerAppInputMethodStatus(false, attemptedAutoEnable);
    }

    private async Task HandleTabSwitchedAsync(HostMessage message, CancellationToken cancellationToken)
    {
        if (message.Payload.CurrentWindowId is null || message.Payload.CurrentTabId is null)
        {
            await SendErrorAsync("tab_switched requires currentWindowId and currentTabId.", cancellationToken);
            return;
        }

        Console.Error.WriteLine(
            $"[als-host] tab_switched previousWindowId={message.Payload.PreviousWindowId?.ToString() ?? "null"} previousTabId={message.Payload.PreviousTabId?.ToString() ?? "null"} currentWindowId={message.Payload.CurrentWindowId.Value} currentTabId={message.Payload.CurrentTabId.Value}");

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
                $"[als-host] Reported previous tab {reportedPreviousKey.Value.WindowId}:{reportedPreviousKey.Value.TabId} does not match tracked active tab {_currentActiveTab.Value.WindowId}:{_currentActiveTab.Value.TabId}. Trusting tracked active tab for remember flow.");
        }

        var tabToRemember = _currentActiveTab ?? reportedPreviousKey;

        if (tabToRemember is not null &&
            tabToRemember.Value != currentKey &&
            !string.IsNullOrWhiteSpace(currentLayoutId))
        {
            _rememberedLayouts[tabToRemember.Value] = currentLayoutId;
            Console.Error.WriteLine(
                $"[als-host] Remembered layout '{currentLayoutId}' for tab {tabToRemember.Value.WindowId}:{tabToRemember.Value.TabId}.");
        }
        else if (tabToRemember is not null && tabToRemember.Value == currentKey)
        {
            Console.Error.WriteLine(
                $"[als-host] Skipped remembering layout because tracked previous tab {tabToRemember.Value.WindowId}:{tabToRemember.Value.TabId} is the same as the current tab.");
        }

        if (!_rememberedLayouts.TryGetValue(currentKey, out var layoutId))
        {
            Console.Error.WriteLine($"[als-host] No remembered layout for tab {currentKey.WindowId}:{currentKey.TabId}.");
            _currentActiveTab = currentKey;
            return;
        }

        await RestoreRememberedLayoutAsync(currentKey, layoutId, currentLayoutId, cancellationToken);
        _currentActiveTab = currentKey;
    }

    private async Task HandleChromeFocusReturnedAsync(HostMessage message, CancellationToken cancellationToken)
    {
        Console.Error.WriteLine(
            $"[als-host] chrome_focus_returned currentWindowId={message.Payload.CurrentWindowId?.ToString() ?? "null"} currentTabId={message.Payload.CurrentTabId?.ToString() ?? "null"}");

        if (_currentActiveTab is null)
        {
            Console.Error.WriteLine("[als-host] Ignoring chrome_focus_returned because no active tab is tracked.");
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
                    $"[als-host] Ignoring chrome_focus_returned for tab {reportedCurrentKey.WindowId}:{reportedCurrentKey.TabId} because tracked active tab is {_currentActiveTab.Value.WindowId}:{_currentActiveTab.Value.TabId}.");
                return;
            }
        }

        var currentKey = _currentActiveTab.Value;
        var currentLayoutId = _keyboardLayoutService.GetCurrentLayoutId();

        if (!_rememberedLayouts.TryGetValue(currentKey, out var layoutId))
        {
            Console.Error.WriteLine($"[als-host] No remembered layout for focused tab {currentKey.WindowId}:{currentKey.TabId}.");
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
                $"[als-host] Layout '{layoutId}' already active for tab {currentKey.WindowId}:{currentKey.TabId}.");

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

        var result = _keyboardLayoutService.TrySwitchTo(layoutId);
        Console.Error.WriteLine(
            $"[als-host] Restore result for tab {currentKey.WindowId}:{currentKey.TabId}: layout='{layoutId}', result={result}.");

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
            Console.Error.WriteLine("[als-host] Ignoring tab_closed without windowId/tabId.");
            return;
        }

        var key = new TabKey(message.Payload.WindowId.Value, message.Payload.TabId.Value);
        _rememberedLayouts.Remove(key);
        Console.Error.WriteLine($"[als-host] Cleared remembered layout for closed tab {key.WindowId}:{key.TabId}.");

        if (_currentActiveTab is not null && _currentActiveTab.Value == key)
        {
            _currentActiveTab = null;
            Console.Error.WriteLine($"[als-host] Cleared current active tab because tab {key.WindowId}:{key.TabId} was closed.");
        }
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
