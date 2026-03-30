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
    private TabKey? _currentActiveTab;

    public NativeMessagingHost(Stream input, Stream output)
    {
        _input = input;
        _output = output;
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
                        Platform = "windows"
                    }
                }, cancellationToken);
                break;
            case "tab_switched":
                await HandleTabSwitchedAsync(message, cancellationToken);
                break;
            case "tab_closed":
                HandleTabClosed(message);
                break;
            default:
                await SendErrorAsync("Unsupported message type.", cancellationToken);
                break;
        }
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

            _currentActiveTab = currentKey;
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

        _currentActiveTab = currentKey;
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
