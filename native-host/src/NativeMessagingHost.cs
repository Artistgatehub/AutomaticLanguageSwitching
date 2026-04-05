using System.Text.Json;

namespace AutomaticLanguageSwitching.NativeHost;

internal sealed class NativeMessagingHost
{
    private const int ProtocolVersion = 1;
    private static readonly int[] RestoreAttemptOffsetsMs = [0, 90, 225];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly Dictionary<TabKey, string> _rememberedLayouts = new();
    private readonly HashSet<TabKey> _skipRememberOverwriteOnce = [];
    private readonly IKeyboardLayoutService _keyboardLayoutService;
    private readonly PerAppInputMethodStatus _perAppInputMethodStatus;
    private TabKey? _currentActiveTab;

    public NativeMessagingHost(Stream input, Stream output)
    {
        _input = input;
        _output = output;
        _keyboardLayoutService = new KeyboardLayoutService();
        _perAppInputMethodStatus = EnsurePerAppInputMethodSetting();
    }

    internal NativeMessagingHost(
        Stream input,
        Stream output,
        IKeyboardLayoutService keyboardLayoutService,
        PerAppInputMethodStatus perAppInputMethodStatus)
    {
        _input = input;
        _output = output;
        _keyboardLayoutService = keyboardLayoutService;
        _perAppInputMethodStatus = perAppInputMethodStatus;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        HostLogger.Log("[als-host] Native Messaging loop started.");
        while (!cancellationToken.IsCancellationRequested)
        {
            var incoming = await ReadMessageAsync(cancellationToken);
            if (incoming is null)
            {
                HostLogger.Log("[als-host] Native Messaging input closed.");
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
                HostLogger.Log($"[als-host] Hello from extension version={message.Payload.ExtensionVersion ?? "unknown"}.");
                await SendAsync(new HostMessage
                {
                    Version = ProtocolVersion,
                    Type = "hello_ack",
                    Payload = new MessagePayload
                    {
                        HostVersion = "0.2.1",
                        Platform = "windows",
                        PerAppInputMethodEnabled = _perAppInputMethodStatus.IsEnabled,
                        AttemptedAutoEnable = _perAppInputMethodStatus.AttemptedAutoEnable
                    }
                }, cancellationToken);
                HostLogger.Log(
                    $"[als-host] hello_ack sent: settingEnabled={_perAppInputMethodStatus.IsEnabled} autoEnableAttempted={_perAppInputMethodStatus.AttemptedAutoEnable}.");

                if (!_perAppInputMethodStatus.IsEnabled)
                {
                    HostLogger.Log("[als-host] Warning sent: Windows per-app input setting still disabled.");
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
            HostLogger.Log("[als-host] Startup check: Windows per-app input setting already enabled.");
            return new PerAppInputMethodStatus(true, false);
        }

        HostLogger.Log("[als-host] Startup check: Windows per-app input setting disabled or unreadable; trying auto-enable.");

        var attemptedAutoEnable = _keyboardLayoutService.TryEnablePerAppInputMethod();
        var finalState = _keyboardLayoutService.IsPerAppInputMethodEnabled();

        if (finalState is true)
        {
            HostLogger.Log(
                $"[als-host] Startup check: Windows per-app input setting enabled after auto-heal. attempted={attemptedAutoEnable}.");
            return new PerAppInputMethodStatus(true, attemptedAutoEnable);
        }

        HostLogger.Log(
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

        var currentLayoutSnapshot = _keyboardLayoutService.GetCurrentLayoutSnapshot();
        var currentLayoutId = currentLayoutSnapshot?.CanonicalLayoutId;
        var existingCurrentLayout = _rememberedLayouts.TryGetValue(currentKey, out var storedCurrentLayout)
            ? storedCurrentLayout
            : null;

        if (reportedPreviousKey is not null &&
            _currentActiveTab is not null &&
            _currentActiveTab.Value != reportedPreviousKey.Value)
        {
            HostLogger.Log(
                $"[als-host] Tab switch: reported previous={FormatTab(reportedPreviousKey)} mismatched tracked={FormatTab(_currentActiveTab)}; using tracked tab.");
        }

        var tabToRemember = _currentActiveTab ?? reportedPreviousKey;
        HostLogger.Log(
            $"[als-host] Tab switch: trackedPrevious={FormatTab(_currentActiveTab)} reportedPrevious={FormatTab(reportedPreviousKey)} previous={FormatTab(tabToRemember)} current={FormatTab(currentKey)} currentHwnd=0x{(currentLayoutSnapshot?.ForegroundWindow.ToInt64() ?? 0):X} currentThreadId={currentLayoutSnapshot?.ForegroundThreadId.ToString() ?? "null"} currentRawLayout={currentLayoutSnapshot?.RawLayoutId ?? "null"} currentCanonicalLayout={currentLayoutId ?? "null"} currentGetKeyboardLayoutName={currentLayoutSnapshot?.GetKeyboardLayoutNameLayoutId ?? "null"} currentStableRemembered={currentLayoutSnapshot?.StableRememberedLayoutId ?? "null"} currentStableSource={currentLayoutSnapshot?.StableRememberedLayoutSource ?? "null"} storedCurrent={existingCurrentLayout ?? "null"}.");

        if (tabToRemember is not null && tabToRemember.Value != currentKey)
        {
            var previousTab = tabToRemember.Value;
            var previousStoredLayout = _rememberedLayouts.TryGetValue(previousTab, out var existingPreviousLayout)
                ? existingPreviousLayout
                : null;

            if (_skipRememberOverwriteOnce.Remove(previousTab))
            {
                HostLogger.Log(
                    $"[als-host] Remember overwrite skipped: tab={FormatTab(previousTab)} reason=previous_restore_failed rawObserved={currentLayoutSnapshot?.RawLayoutId ?? "null"} canonicalObserved={currentLayoutId ?? "null"} getKeyboardLayoutName={currentLayoutSnapshot?.GetKeyboardLayoutNameLayoutId ?? "null"} stableCandidate={currentLayoutSnapshot?.StableRememberedLayoutId ?? "null"} stableSource={currentLayoutSnapshot?.StableRememberedLayoutSource ?? "null"} previousStored={previousStoredLayout ?? "null"}.");
            }
            else if (!string.IsNullOrWhiteSpace(currentLayoutSnapshot?.StableRememberedLayoutId))
            {
                var observedSnapshot = currentLayoutSnapshot.Value;
                _rememberedLayouts[previousTab] = observedSnapshot.StableRememberedLayoutId!;
                HostLogger.Log(
                    $"[als-host] Remember: tab={FormatTab(tabToRemember)} hwnd=0x{observedSnapshot.ForegroundWindow.ToInt64():X} threadId={observedSnapshot.ForegroundThreadId} rawObserved={observedSnapshot.RawLayoutId} canonicalObserved={observedSnapshot.CanonicalLayoutId} getKeyboardLayoutName={observedSnapshot.GetKeyboardLayoutNameLayoutId ?? "null"} stableRemembered={observedSnapshot.StableRememberedLayoutId} stableSource={observedSnapshot.StableRememberedLayoutSource} previousStored={previousStoredLayout ?? "null"} overwritten={(!string.Equals(previousStoredLayout, observedSnapshot.StableRememberedLayoutId, StringComparison.OrdinalIgnoreCase)).ToString().ToLowerInvariant()}.");
            }
            else
            {
                HostLogger.Log(
                    $"[als-host] Remember overwrite skipped: tab={FormatTab(tabToRemember)} reason=transient_or_unmappable rawObserved={currentLayoutSnapshot?.RawLayoutId ?? "null"} canonicalObserved={currentLayoutId ?? "null"} getKeyboardLayoutName={currentLayoutSnapshot?.GetKeyboardLayoutNameLayoutId ?? "null"} stableCandidate=null stableSource={currentLayoutSnapshot?.StableRememberedLayoutSource ?? "null"} previousStored={previousStoredLayout ?? "null"}.");
            }
        }
        else if (tabToRemember is not null && tabToRemember.Value == currentKey)
        {
            HostLogger.Log(
                $"[als-host] Remember ignored: previous tab {FormatTab(tabToRemember)} is the same as current.");
        }
        else if (tabToRemember is not null)
        {
            HostLogger.Log(
                $"[als-host] Remember ignored: no current layout available for previous tab {FormatTab(tabToRemember)} rawObserved={currentLayoutSnapshot?.RawLayoutId ?? "null"} canonicalObserved={currentLayoutId ?? "null"}.");
        }

        if (!_rememberedLayouts.TryGetValue(currentKey, out var layoutId))
        {
            HostLogger.Log(
                $"[als-host] Restore skipped: no remembered layout for {FormatTab(currentKey)} currentLayout={currentLayoutId ?? "null"}.");
            _currentActiveTab = currentKey;
            return;
        }

        await RestoreRememberedLayoutAsync(tabToRemember, currentKey, layoutId, currentLayoutId, "tab_switched", cancellationToken);
        _currentActiveTab = currentKey;
    }

    private async Task HandleChromeFocusReturnedAsync(HostMessage message, CancellationToken cancellationToken)
    {
        if (_currentActiveTab is null)
        {
            HostLogger.Log("[als-host] Focus return ignored: no active tab is tracked.");
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
                HostLogger.Log(
                    $"[als-host] Focus return ignored: reported={FormatTab(reportedCurrentKey)} tracked={FormatTab(_currentActiveTab)}.");
                return;
            }
        }

        var currentKey = _currentActiveTab.Value;
        HostLogger.Log($"[als-host] Focus return: tab={FormatTab(currentKey)}.");
        var currentLayoutId = _keyboardLayoutService.GetCurrentLayoutId();

        if (!_rememberedLayouts.TryGetValue(currentKey, out var layoutId))
        {
            HostLogger.Log(
                $"[als-host] Focus return restore skipped: no remembered layout for {FormatTab(currentKey)} currentLayout={currentLayoutId ?? "null"}.");
            return;
        }

        await RestoreRememberedLayoutAsync(null, currentKey, layoutId, currentLayoutId, "chrome_focus_returned", cancellationToken);
    }

    private async Task RestoreRememberedLayoutAsync(
        TabKey? previousKey,
        TabKey currentKey,
        string layoutId,
        string? currentLayoutId,
        string trigger,
        CancellationToken cancellationToken)
    {
        var finalStoredLayoutId = _keyboardLayoutService.TryGetStableLayoutIdForStorage(layoutId);
        if (finalStoredLayoutId is null)
        {
            HostLogger.Log(
                $"[als-host] Restore skipped: trigger={trigger} previous={FormatTab(previousKey)} current={FormatTab(currentKey)} savedLayout={layoutId} reason=non_stable_saved_layout.");
            await SendLayoutRestoreResultAsync(currentKey, null, "failed", cancellationToken);
            return;
        }

        if (!string.Equals(finalStoredLayoutId, layoutId, StringComparison.OrdinalIgnoreCase))
        {
            HostLogger.Log(
                $"[als-host] Restore saved layout normalized: trigger={trigger} previous={FormatTab(previousKey)} current={FormatTab(currentKey)} savedLayout={layoutId} finalSavedLayout={finalStoredLayoutId}.");
        }

        if (string.Equals(finalStoredLayoutId, currentLayoutId, StringComparison.OrdinalIgnoreCase))
        {
            _skipRememberOverwriteOnce.Remove(currentKey);
            HostLogger.Log(
                $"[als-host] Restore skipped: trigger={trigger} previous={FormatTab(previousKey)} current={FormatTab(currentKey)} savedLayout={finalStoredLayoutId} currentLayout={currentLayoutId} reason=already_active.");

            await SendLayoutRestoreResultAsync(currentKey, finalStoredLayoutId, "applied", cancellationToken);
            return;
        }

        HostLogger.Log(
            $"[als-host] Restore begin: trigger={trigger} previous={FormatTab(previousKey)} current={FormatTab(currentKey)} savedLayout={finalStoredLayoutId} currentLayout={currentLayoutId ?? "null"} logFile={HostLogger.LogFilePath}.");

        LayoutSwitchAttemptResult? finalAttempt = null;
        var lastOffset = 0;

        for (var attemptIndex = 0; attemptIndex < RestoreAttemptOffsetsMs.Length; attemptIndex++)
        {
            var offset = RestoreAttemptOffsetsMs[attemptIndex];
            var waitMs = offset - lastOffset;
            if (waitMs > 0)
            {
                await Task.Delay(waitMs, cancellationToken);
            }

            var attempt = _keyboardLayoutService.TrySwitchTo(
                finalStoredLayoutId,
                new RestoreAttemptContext(
                    trigger,
                    FormatTab(previousKey),
                    FormatTab(currentKey),
                    attemptIndex + 1,
                    offset));
            finalAttempt = attempt;
            lastOffset = offset;

            HostLogger.Log(
                $"[als-host] Restore attempt result: trigger={trigger} previous={FormatTab(previousKey)} current={FormatTab(currentKey)} attempt={attempt.AttemptNumber} delayMs={attempt.AttemptDelayMs} storedLayout={finalStoredLayoutId} before={attempt.LayoutBeforeRestore ?? "null"} requested={attempt.RequestedLayoutId ?? "null"} canonicalRequested={attempt.CanonicalRequestedLayoutId ?? "null"} loadResult={attempt.RawLoadKeyboardLayoutResult} loadCandidate={attempt.LoadCandidateKlid ?? "null"} loadRawHkl={attempt.LoadKeyboardLayoutRawHkl ?? "null"} loadCanonicalHkl={attempt.LoadKeyboardLayoutCanonicalHkl ?? "null"} activationResult={attempt.RawActivationResult} activationResponse={attempt.RawActivationResponse} activationWin32={attempt.ActivationWin32Error?.ToString() ?? "null"} immediate={attempt.ImmediateEffectiveLayoutId ?? "null"} result={attempt.Result} failureReason={attempt.FailureReason ?? "null"} retryRecommended={attempt.RetryRecommended.ToString().ToLowerInvariant()}.");

            if (attempt.Result == LayoutSwitchResult.Applied)
            {
                _skipRememberOverwriteOnce.Remove(currentKey);
                HostLogger.Log(
                    $"[als-host] Restore succeeded: trigger={trigger} previous={FormatTab(previousKey)} current={FormatTab(currentKey)} savedLayout={finalStoredLayoutId} attempt={attempt.AttemptNumber}.");
                await SendLayoutRestoreResultAsync(currentKey, finalStoredLayoutId, "applied", cancellationToken);
                return;
            }

            if (!attempt.RetryRecommended)
            {
                break;
            }
        }

        var finalResult = finalAttempt?.Result switch
        {
            LayoutSwitchResult.Unavailable => "unavailable",
            _ => "failed"
        };

        HostLogger.Log(
            $"[als-host] Restore finished without verified match: trigger={trigger} previous={FormatTab(previousKey)} current={FormatTab(currentKey)} savedLayout={finalStoredLayoutId} currentLayout={currentLayoutId ?? "null"} finalResult={finalResult} attempts={finalAttempt?.AttemptNumber ?? 0} finalImmediate={finalAttempt?.ImmediateEffectiveLayoutId ?? "null"}.");

        _skipRememberOverwriteOnce.Add(currentKey);
        HostLogger.Log(
            $"[als-host] Remember protection armed: tab={FormatTab(currentKey)} reason=restore_failed preservedStoredLayout={finalStoredLayoutId}.");

        await SendLayoutRestoreResultAsync(currentKey, finalStoredLayoutId, finalResult, cancellationToken);
    }

    private void HandleTabClosed(HostMessage message)
    {
        if (message.Payload.WindowId is null || message.Payload.TabId is null)
        {
            HostLogger.Log("[als-host] Tab close ignored: missing windowId/tabId.");
            return;
        }

        var key = new TabKey(message.Payload.WindowId.Value, message.Payload.TabId.Value);
        _rememberedLayouts.Remove(key);
        HostLogger.Log($"[als-host] Tab closed: cleared remembered layout for {FormatTab(key)}.");

        if (_currentActiveTab is not null && _currentActiveTab.Value == key)
        {
            _currentActiveTab = null;
            HostLogger.Log($"[als-host] Active tab cleared because {FormatTab(key)} was closed.");
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

    private Task SendLayoutRestoreResultAsync(
        TabKey currentKey,
        string? layoutId,
        string result,
        CancellationToken cancellationToken)
    {
        return SendAsync(new HostMessage
        {
            Version = ProtocolVersion,
            Type = "layout_restore_result",
            Payload = new MessagePayload
            {
                WindowId = currentKey.WindowId,
                TabId = currentKey.TabId,
                LayoutId = layoutId,
                Result = result
            }
        }, cancellationToken);
    }

    private Task SendErrorAsync(string text, CancellationToken cancellationToken)
    {
        HostLogger.Log($"[als-host] Error: {text}");
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
