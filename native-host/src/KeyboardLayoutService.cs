using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AutomaticLanguageSwitching.NativeHost;

internal sealed class KeyboardLayoutService
{
    private const uint SpiGetThreadLocalInputSettings = 0x104E;
    private const uint SpiSetThreadLocalInputSettings = 0x104F;
    private const uint SpifUpdateIniFile = 0x0001;
    private const uint SpifSendChange = 0x0002;
    private const string KeyboardLayoutRegistryPath = @"Keyboard Layout\Preload";
    private const string KeyboardLayoutSubstitutesRegistryPath = @"Keyboard Layout\Substitutes";
    private const uint KlfActivate = 0x00000001;
    private const uint KlfSubstituteOk = 0x00000002;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint WmInputLangChangeRequest = 0x0050;

    private static readonly int[] VerificationOffsetsMs = [0, 50, 150, 300];
    private static readonly TimeSpan InstalledLayoutsCacheTtl = TimeSpan.FromSeconds(5);
    private static readonly Regex LayoutIdPattern = new("^[0-9A-F]{8}$", RegexOptions.Compiled);

    private readonly object _cacheLock = new();
    private IReadOnlyCollection<string>? _installedLayoutIdsCache;
    private DateTimeOffset _installedLayoutIdsCachedAt = DateTimeOffset.MinValue;
    private IReadOnlyCollection<string>? _configuredLayoutIdsCache;
    private DateTimeOffset _configuredLayoutIdsCachedAt = DateTimeOffset.MinValue;

    public IReadOnlyCollection<string> GetInstalledLayoutIds()
    {
        lock (_cacheLock)
        {
            if (_installedLayoutIdsCache is not null &&
                DateTimeOffset.UtcNow - _installedLayoutIdsCachedAt < InstalledLayoutsCacheTtl)
            {
                return _installedLayoutIdsCache;
            }

            _installedLayoutIdsCache = ReadInstalledLayoutIds();
            _installedLayoutIdsCachedAt = DateTimeOffset.UtcNow;

            return _installedLayoutIdsCache;
        }
    }

    public IReadOnlyCollection<string> GetConfiguredLayoutIds()
    {
        lock (_cacheLock)
        {
            if (_configuredLayoutIdsCache is not null &&
                DateTimeOffset.UtcNow - _configuredLayoutIdsCachedAt < InstalledLayoutsCacheTtl)
            {
                return _configuredLayoutIdsCache;
            }

            _configuredLayoutIdsCache = ReadInstalledLayoutIdsFromRegistry();
            _configuredLayoutIdsCachedAt = DateTimeOffset.UtcNow;

            return _configuredLayoutIdsCache;
        }
    }

    public bool IsLayoutInstalled(string layoutId)
    {
        var normalized = NormalizeLayoutId(layoutId);
        if (normalized is null)
        {
            return false;
        }

        return GetInstalledLayoutIds().Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsValidLayoutId(string layoutId)
    {
        return NormalizeLayoutId(layoutId) is not null;
    }

    public bool? IsPerAppInputMethodEnabled()
    {
        var enabled = 0;
        var success = SystemParametersInfo(
            SpiGetThreadLocalInputSettings,
            0,
            ref enabled,
            0);

        if (!success)
        {
            var error = Marshal.GetLastWin32Error();
            HostLogger.Log(
                $"[als-host] Windows per-app input setting read failed. win32={error}");
            return null;
        }

        var isEnabled = enabled != 0;
        HostLogger.Log($"[als-host] Windows per-app input setting: enabled={isEnabled}.");
        return isEnabled;
    }

    public bool TryEnablePerAppInputMethod()
    {
        var enabled = 1;
        var success = SystemParametersInfo(
            SpiSetThreadLocalInputSettings,
            0,
            ref enabled,
            SpifUpdateIniFile | SpifSendChange);

        if (!success)
        {
            var error = Marshal.GetLastWin32Error();
            HostLogger.Log(
                $"[als-host] Windows per-app input auto-enable failed. win32={error}");
            return false;
        }

        HostLogger.Log("[als-host] Windows per-app input auto-enable requested.");
        return true;
    }

    public string? GetCurrentLayoutId()
    {
        return GetCurrentLayoutSnapshot()?.CanonicalLayoutId;
    }

    public ObservedLayoutSnapshot? GetCurrentLayoutSnapshot()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            HostLogger.Log("[als-host] GetCurrentLayoutSnapshot failed: GetForegroundWindow returned zero.");
            return null;
        }

        var threadId = GetWindowThreadProcessId(foregroundWindow, out _);
        if (threadId == 0)
        {
            HostLogger.Log("[als-host] GetCurrentLayoutSnapshot failed: GetWindowThreadProcessId returned zero.");
            return null;
        }

        var keyboardLayout = GetKeyboardLayout(threadId);
        if (keyboardLayout == IntPtr.Zero)
        {
            HostLogger.Log("[als-host] GetCurrentLayoutSnapshot failed: GetKeyboardLayout returned zero.");
            return null;
        }

        var rawLayoutId = (keyboardLayout.ToInt64() & 0xFFFFFFFFL).ToString("X8");
        var canonicalLayoutId = GetPreferredRestoreLayoutId(rawLayoutId);
        var keyboardLayoutNameLayoutId = TryGetKeyboardLayoutNameLayoutId();
        var stableResolution = ResolveStableLayoutForObservation(rawLayoutId, canonicalLayoutId, keyboardLayoutNameLayoutId);

        if (!string.Equals(rawLayoutId, canonicalLayoutId, StringComparison.OrdinalIgnoreCase))
        {
            HostLogger.Log(
                $"[als-host] Current layout canonicalized raw={rawLayoutId} restore={canonicalLayoutId}.");
        }

        HostLogger.Log(
            $"[als-host] Observation: hwnd=0x{foregroundWindow.ToInt64():X} threadId={threadId} hklRaw={rawLayoutId} hklCanonical={canonicalLayoutId} getKeyboardLayoutName={keyboardLayoutNameLayoutId ?? "null"} stableSource={stableResolution.Source} stableRemembered={stableResolution.LayoutId ?? "null"}.");

        if (stableResolution.LayoutId is null)
        {
            HostLogger.Log(
                $"[als-host] Observation stable resolution failed: hwnd=0x{foregroundWindow.ToInt64():X} threadId={threadId} raw={rawLayoutId} canonical={canonicalLayoutId} getKeyboardLayoutName={keyboardLayoutNameLayoutId ?? "null"}.");
        }

        return new ObservedLayoutSnapshot(
            foregroundWindow,
            threadId,
            rawLayoutId,
            canonicalLayoutId,
            keyboardLayoutNameLayoutId,
            stableResolution.LayoutId,
            stableResolution.Source);
    }

    public string? TryGetStableLayoutIdForStorage(string layoutId)
    {
        var configuredLayoutIds = GetConfiguredLayoutIds();
        var installedLayoutIds = GetInstalledLayoutIds();
        var resolution = ResolveStableLayoutCandidate("storage", layoutId, configuredLayoutIds, installedLayoutIds);
        return resolution.LayoutId;
    }

    private StableLayoutResolution ResolveStableLayoutForObservation(
        string rawLayoutId,
        string canonicalLayoutId,
        string? keyboardLayoutNameLayoutId)
    {
        var configuredLayoutIds = GetConfiguredLayoutIds();
        var installedLayoutIds = GetInstalledLayoutIds();

        foreach (var candidate in GetObservationCandidates(rawLayoutId, canonicalLayoutId, keyboardLayoutNameLayoutId))
        {
            var resolution = ResolveStableLayoutCandidate(candidate.Source, candidate.LayoutId, configuredLayoutIds, installedLayoutIds);
            if (resolution.LayoutId is not null)
            {
                HostLogger.Log(
                    $"[als-host] Observation winner: source={candidate.Source} candidate={candidate.LayoutId ?? "null"} finalStable={resolution.LayoutId} reason={resolution.Source}.");
                return resolution;
            }

            HostLogger.Log(
                $"[als-host] Observation candidate rejected: source={candidate.Source} candidate={candidate.LayoutId ?? "null"} reason={resolution.Source}.");
        }

        return new StableLayoutResolution(null, "none");
    }

    private StableLayoutResolution ResolveStableLayoutCandidate(
        string source,
        string? candidateLayoutId,
        IReadOnlyCollection<string> configuredLayoutIds,
        IReadOnlyCollection<string> installedLayoutIds)
    {
        var normalized = NormalizeLayoutId(candidateLayoutId ?? string.Empty);
        if (normalized is null)
        {
            return new StableLayoutResolution(null, $"{source}:invalid");
        }

        var stableConfiguredLayoutIds = GetStrictStableLayoutIds(configuredLayoutIds);
        var stableInstalledLayoutIds = GetStrictStableLayoutIds(installedLayoutIds);
        var finalizedExact = FinalizeStableLayoutId(normalized, stableConfiguredLayoutIds, stableInstalledLayoutIds);

        if (finalizedExact is not null && string.Equals(finalizedExact, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return new StableLayoutResolution(finalizedExact, $"{source}:stable-exact");
        }

        var canonical = GetPreferredRestoreLayoutId(normalized);
        var finalizedCanonical = FinalizeStableLayoutId(canonical, stableConfiguredLayoutIds, stableInstalledLayoutIds);
        if (finalizedCanonical is not null)
        {
            return new StableLayoutResolution(finalizedCanonical, $"{source}:stable-canonical");
        }

        var languageFallback = GetLanguageFallbackLayoutId(normalized);
        var finalizedLanguageFallback = FinalizeStableLayoutId(languageFallback, stableConfiguredLayoutIds, stableInstalledLayoutIds);
        if (finalizedLanguageFallback is not null)
        {
            return new StableLayoutResolution(finalizedLanguageFallback, $"{source}:stable-language");
        }

        var lowWordMatch = FindLayoutByLanguageSuffix(normalized, stableConfiguredLayoutIds)
            ?? FindLayoutByLanguageSuffix(normalized, stableInstalledLayoutIds);
        if (lowWordMatch is not null)
        {
            return new StableLayoutResolution(lowWordMatch, $"{source}:suffix-match");
        }

        return new StableLayoutResolution(null, $"{source}:transient-or-non-normalized");
    }

    private static IEnumerable<(string Source, string? LayoutId)> GetObservationCandidates(
        string rawLayoutId,
        string canonicalLayoutId,
        string? keyboardLayoutNameLayoutId)
    {
        yield return ("foreground-hkl-raw", rawLayoutId);
        yield return ("foreground-hkl-canonical", canonicalLayoutId);

        if (!string.IsNullOrWhiteSpace(keyboardLayoutNameLayoutId))
        {
            yield return ("getkeyboardlayoutname", keyboardLayoutNameLayoutId);
        }
    }

    private static string? FindLayoutByLanguageSuffix(string layoutId, IEnumerable<string> candidates)
    {
        var suffix = layoutId[4..];
        return candidates
            .Where(candidate => candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static HashSet<string> GetStrictStableLayoutIds(IEnumerable<string> layoutIds)
    {
        var stableLayoutIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var layoutId in layoutIds)
        {
            var strictStableLayoutId = TryNormalizeToStrictStableKlid(layoutId);
            if (strictStableLayoutId is not null)
            {
                stableLayoutIds.Add(strictStableLayoutId);
            }
        }

        return stableLayoutIds;
    }

    private static string? FinalizeStableLayoutId(
        string? candidateLayoutId,
        IReadOnlyCollection<string> stableConfiguredLayoutIds,
        IReadOnlyCollection<string> stableInstalledLayoutIds)
    {
        var strictStableLayoutId = TryNormalizeToStrictStableKlid(candidateLayoutId);
        if (strictStableLayoutId is null)
        {
            return null;
        }

        if (stableConfiguredLayoutIds.Contains(strictStableLayoutId, StringComparer.OrdinalIgnoreCase))
        {
            return strictStableLayoutId;
        }

        if (stableInstalledLayoutIds.Contains(strictStableLayoutId, StringComparer.OrdinalIgnoreCase))
        {
            return strictStableLayoutId;
        }

        return null;
    }

    private static string? TryNormalizeToStrictStableKlid(string? layoutId)
    {
        var normalized = NormalizeLayoutId(layoutId ?? string.Empty);
        if (normalized is null)
        {
            return null;
        }

        return GetLanguageFallbackLayoutId(normalized);
    }

    public LayoutSwitchAttemptResult TrySwitchTo(string layoutId, RestoreAttemptContext context)
    {
        var result = new LayoutSwitchAttemptResult
        {
            AttemptNumber = context.AttemptNumber,
            AttemptDelayMs = context.AttemptDelayMs,
            Trigger = context.Trigger,
            PreviousTabId = context.PreviousTabId,
            CurrentTabId = context.CurrentTabId,
            StoredLayoutId = layoutId,
            LayoutBeforeRestore = GetCurrentLayoutId()
        };

        var normalized = NormalizeLayoutId(layoutId);
        if (normalized is null)
        {
            HostLogger.Log(
                $"[als-host] Restore attempt={context.AttemptNumber} trigger={context.Trigger} previous={context.PreviousTabId} current={context.CurrentTabId} failed: invalid layoutId stored={layoutId} before={result.LayoutBeforeRestore ?? "null"}.");
            return result with
            {
                Result = LayoutSwitchResult.Failed,
                FailureReason = "invalid_layout",
                RetryRecommended = false
            };
        }

        var restoreLayoutId = TryGetStableLayoutIdForStorage(normalized);
        if (restoreLayoutId is null)
        {
            HostLogger.Log(
                $"[als-host] Restore attempt={context.AttemptNumber} trigger={context.Trigger} previous={context.PreviousTabId} current={context.CurrentTabId} rejected: storedLayout={layoutId} normalized={normalized} reason=non_stable_final_candidate.");
            return result with
            {
                RequestedLayoutId = normalized,
                Result = LayoutSwitchResult.Failed,
                FailureReason = "non_stable_restore_target",
                RetryRecommended = false
            };
        }

        result = result with
        {
            RequestedLayoutId = normalized,
            CanonicalRequestedLayoutId = restoreLayoutId
        };

        if (!string.Equals(restoreLayoutId, normalized, StringComparison.OrdinalIgnoreCase))
        {
            HostLogger.Log(
                $"[als-host] Restore request canonicalized requested={normalized} restore={restoreLayoutId}.");
        }

        HostLogger.Log(
            $"[als-host] Restore attempt={context.AttemptNumber} trigger={context.Trigger} previous={context.PreviousTabId} current={context.CurrentTabId} delayMs={context.AttemptDelayMs} stored={layoutId} requested={normalized} canonicalRequested={restoreLayoutId} before={result.LayoutBeforeRestore ?? "null"}.");

        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            HostLogger.Log("[als-host] TrySwitchTo failed: GetForegroundWindow returned zero.");
            return result with
            {
                Result = LayoutSwitchResult.Failed,
                FailureReason = "no_foreground_window",
                RetryRecommended = true
            };
        }

        if (!IsChromeForegroundWindow(foregroundWindow))
        {
            HostLogger.Log("[als-host] Restore ignored: foreground window is not chrome.exe.");
            return result with
            {
                Result = LayoutSwitchResult.Failed,
                FailureReason = "foreground_not_chrome",
                RetryRecommended = true
            };
        }

        if (!TryLoadKeyboardLayoutHandle(restoreLayoutId, out var loadResolution))
        {
            return result with
            {
                Result = LayoutSwitchResult.Unavailable,
                FailureReason = "load_unavailable",
                RetryRecommended = false
            };
        }

        result = result with
        {
            LoadCandidateKlid = loadResolution.CandidateKlid,
            RawLoadKeyboardLayoutResult = loadResolution.Handle.ToInt64(),
            LoadKeyboardLayoutRawHkl = loadResolution.RawHkl,
            LoadKeyboardLayoutCanonicalHkl = loadResolution.CanonicalHkl
        };

        var messageResult = SendMessageTimeout(
            foregroundWindow,
            WmInputLangChangeRequest,
            IntPtr.Zero,
            loadResolution.Handle,
            SmtoAbortIfHung,
            1000,
            out var activationResponse);

        result = result with
        {
            RawActivationResult = messageResult.ToInt64(),
            RawActivationResponse = activationResponse.ToInt64()
        };

        if (messageResult == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            HostLogger.Log(
                $"[als-host] SendMessageTimeout failed for {restoreLayoutId}. GetLastWin32Error={error}");
            return result with
            {
                Result = LayoutSwitchResult.Failed,
                FailureReason = "activation_send_failed",
                RetryRecommended = true,
                ActivationWin32Error = error
            };
        }

        HostLogger.Log(
            $"[als-host] SendMessageTimeout succeeded for {restoreLayoutId}. rawResult={messageResult.ToInt64()} response={activationResponse.ToInt64()}.");

        var verificationSamples = CaptureVerificationSamples();
        var immediateLayout = verificationSamples.FirstOrDefault(sample => sample.OffsetMs == 0).EffectiveLayoutId;
        var matched = verificationSamples.Any(sample =>
            string.Equals(sample.EffectiveLayoutId, restoreLayoutId, StringComparison.OrdinalIgnoreCase));

        result = result with
        {
            ImmediateEffectiveLayoutId = immediateLayout,
            VerificationSamples = verificationSamples,
            Result = matched ? LayoutSwitchResult.Applied : LayoutSwitchResult.Failed,
            FailureReason = matched ? null : "verification_mismatch",
            RetryRecommended = !matched
        };

        foreach (var sample in verificationSamples)
        {
            HostLogger.Log(
                $"[als-host] Restore verify attempt={context.AttemptNumber} trigger={context.Trigger} previous={context.PreviousTabId} current={context.CurrentTabId} offsetMs={sample.OffsetMs} effective={sample.EffectiveLayoutId ?? "null"} target={restoreLayoutId}.");
        }

        if (matched)
        {
            HostLogger.Log(
                $"[als-host] Restore confirmed attempt={context.AttemptNumber} trigger={context.Trigger} previous={context.PreviousTabId} current={context.CurrentTabId} target={restoreLayoutId}.");
        }
        else
        {
            HostLogger.Log(
                $"[als-host] Restore verification mismatch attempt={context.AttemptNumber} trigger={context.Trigger} previous={context.PreviousTabId} current={context.CurrentTabId} target={restoreLayoutId} immediate={immediateLayout ?? "null"}.");
        }

        return result;
    }

    public static string? NormalizeLayoutId(string layoutId)
    {
        if (string.IsNullOrWhiteSpace(layoutId))
        {
            return null;
        }

        var normalized = layoutId.Trim().ToUpperInvariant();
        return LayoutIdPattern.IsMatch(normalized) ? normalized : null;
    }

    private static IReadOnlyCollection<string> ReadInstalledLayoutIds()
    {
        var runtimeLayouts = GetRuntimeLoadedLayoutIds();
        if (runtimeLayouts.Count > 0)
        {
            return new ReadOnlyCollection<string>(
                runtimeLayouts.OrderBy(value => value, StringComparer.Ordinal).ToArray());
        }

        return ReadInstalledLayoutIdsFromRegistry();
    }

    private static HashSet<string> GetRuntimeLoadedLayoutIds()
    {
        var count = GetKeyboardLayoutList(0, null);
        if (count <= 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var handles = new IntPtr[count];
        var copied = GetKeyboardLayoutList(handles.Length, handles);
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < copied; i++)
        {
            var value = handles[i].ToInt64() & 0xFFFFFFFFL;
            installed.Add(value.ToString("X8"));
        }

        return installed;
    }

    private static IReadOnlyCollection<string> ReadInstalledLayoutIdsFromRegistry()
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var preloadKey = Registry.CurrentUser.OpenSubKey(KeyboardLayoutRegistryPath);
        using var substitutesKey = Registry.CurrentUser.OpenSubKey(KeyboardLayoutSubstitutesRegistryPath);

        if (preloadKey is null)
        {
            return Array.Empty<string>();
        }

        foreach (var valueName in preloadKey.GetValueNames())
        {
            if (preloadKey.GetValue(valueName) is not string rawLayoutId)
            {
                continue;
            }

            var normalized = NormalizeLayoutId(rawLayoutId);
            if (normalized is null)
            {
                continue;
            }

            if (substitutesKey?.GetValue(normalized) is string substitutedLayoutId)
            {
                normalized = NormalizeLayoutId(substitutedLayoutId) ?? normalized;
            }

            installed.Add(normalized);
        }

        return new ReadOnlyCollection<string>(
            installed.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    // Windows 11 can report transient HKL values such as F0A80422 for the active thread.
    // Those identify the current runtime state but are not stable future restore targets,
    // so prefer a configured KLID when we can map the HKL back to one.
    public string GetPreferredRestoreLayoutId(string layoutId)
    {
        var normalized = NormalizeLayoutId(layoutId);
        if (normalized is null)
        {
            return layoutId;
        }

        var configuredLayoutIds = GetConfiguredLayoutIds();
        if (configuredLayoutIds.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return normalized;
        }

        var languageFallback = GetLanguageFallbackLayoutId(normalized);
        if (configuredLayoutIds.Contains(languageFallback, StringComparer.OrdinalIgnoreCase))
        {
            return languageFallback;
        }

        return normalized;
    }

    private static bool IsChromeForegroundWindow(IntPtr foregroundWindow)
    {
        _ = GetWindowThreadProcessId(foregroundWindow, out var processId);
        if (processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return string.Equals(process.ProcessName, "chrome", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private bool TryLoadKeyboardLayoutHandle(string targetLayoutId, out LoadKeyboardLayoutResolution loadResolution)
    {
        foreach (var candidateKlid in GetLoadKeyboardLayoutCandidates(targetLayoutId))
        {
            var candidateHandle = LoadKeyboardLayout(candidateKlid, KlfActivate | KlfSubstituteOk);
            if (candidateHandle == IntPtr.Zero)
            {
                var loadError = Marshal.GetLastWin32Error();
                HostLogger.Log(
                    $"[als-host] LoadKeyboardLayout failed for {candidateKlid} while targeting {targetLayoutId}. win32={loadError}");
                continue;
            }

            var rawCandidateLayoutId = NormalizeHkl(candidateHandle);
            var canonicalCandidateLayoutId = rawCandidateLayoutId is null
                ? null
                : GetPreferredRestoreLayoutId(rawCandidateLayoutId);

            HostLogger.Log(
                $"[als-host] LoadKeyboardLayout succeeded for candidate={candidateKlid} target={targetLayoutId} rawHkl={rawCandidateLayoutId ?? "null"} canonicalHkl={canonicalCandidateLayoutId ?? "null"}.");

            if (string.Equals(canonicalCandidateLayoutId, targetLayoutId, StringComparison.OrdinalIgnoreCase))
            {
                loadResolution = new LoadKeyboardLayoutResolution(
                    candidateKlid,
                    candidateHandle,
                    rawCandidateLayoutId,
                    canonicalCandidateLayoutId);
                return true;
            }

            if (string.Equals(candidateKlid, targetLayoutId, StringComparison.OrdinalIgnoreCase))
            {
                HostLogger.Log(
                    $"[als-host] Accepting loaded layout for target={targetLayoutId} despite HKL mismatch rawHkl={rawCandidateLayoutId ?? "null"} canonicalHkl={canonicalCandidateLayoutId ?? "null"}.");
                loadResolution = new LoadKeyboardLayoutResolution(
                    candidateKlid,
                    candidateHandle,
                    rawCandidateLayoutId,
                    canonicalCandidateLayoutId);
                return true;
            }
        }

        HostLogger.Log(
            $"[als-host] Restore unavailable: could not load layout {targetLayoutId}.");
        loadResolution = default;
        return false;
    }

    private static IEnumerable<string> GetLoadKeyboardLayoutCandidates(string targetLayoutId)
    {
        yield return targetLayoutId;

        var languageFallback = GetLanguageFallbackLayoutId(targetLayoutId);
        if (!string.Equals(languageFallback, targetLayoutId, StringComparison.OrdinalIgnoreCase))
        {
            yield return languageFallback;
        }
    }

    private static string GetLanguageFallbackLayoutId(string layoutId)
    {
        return $"0000{layoutId[4..]}";
    }

    private static string? TryGetKeyboardLayoutNameLayoutId()
    {
        var buffer = new StringBuilder(9);
        if (!GetKeyboardLayoutName(buffer))
        {
            return null;
        }

        return NormalizeLayoutId(buffer.ToString());
    }

    private static string? NormalizeHkl(IntPtr keyboardLayout)
    {
        if (keyboardLayout == IntPtr.Zero)
        {
            return null;
        }

        return NormalizeLayoutId((keyboardLayout.ToInt64() & 0xFFFFFFFFL).ToString("X8"));
    }

    private List<LayoutVerificationSample> CaptureVerificationSamples()
    {
        var samples = new List<LayoutVerificationSample>(VerificationOffsetsMs.Length);
        var previousOffset = 0;

        foreach (var offset in VerificationOffsetsMs)
        {
            var sleepDuration = offset - previousOffset;
            if (sleepDuration > 0)
            {
                Thread.Sleep(sleepDuration);
            }

            samples.Add(new LayoutVerificationSample(offset, GetCurrentLayoutId()));
            previousOffset = offset;
        }

        return samples;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "LoadKeyboardLayoutW")]
    private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int nBuff, IntPtr[]? lpList);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardLayoutName(StringBuilder pwszKLID);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint uiAction,
        uint uiParam,
        ref int pvParam,
        uint fWinIni);
}

internal enum LayoutSwitchResult
{
    Applied,
    Unavailable,
    Failed
}

internal readonly record struct RestoreAttemptContext(
    string Trigger,
    string PreviousTabId,
    string CurrentTabId,
    int AttemptNumber,
    int AttemptDelayMs);

internal readonly record struct LayoutVerificationSample(
    int OffsetMs,
    string? EffectiveLayoutId);

internal readonly record struct LoadKeyboardLayoutResolution(
    string CandidateKlid,
    IntPtr Handle,
    string? RawHkl,
    string? CanonicalHkl);

internal readonly record struct ObservedLayoutSnapshot(
    IntPtr ForegroundWindow,
    uint ForegroundThreadId,
    string RawLayoutId,
    string CanonicalLayoutId,
    string? GetKeyboardLayoutNameLayoutId,
    string? StableRememberedLayoutId,
    string StableRememberedLayoutSource);

internal readonly record struct StableLayoutResolution(
    string? LayoutId,
    string Source);

internal sealed record LayoutSwitchAttemptResult
{
    public int AttemptNumber { get; init; }
    public int AttemptDelayMs { get; init; }
    public string Trigger { get; init; } = "unknown";
    public string PreviousTabId { get; init; } = "null";
    public string CurrentTabId { get; init; } = "null";
    public string StoredLayoutId { get; init; } = string.Empty;
    public string? RequestedLayoutId { get; init; }
    public string? CanonicalRequestedLayoutId { get; init; }
    public string? LayoutBeforeRestore { get; init; }
    public string? LoadCandidateKlid { get; init; }
    public long RawLoadKeyboardLayoutResult { get; init; }
    public string? LoadKeyboardLayoutRawHkl { get; init; }
    public string? LoadKeyboardLayoutCanonicalHkl { get; init; }
    public long RawActivationResult { get; init; }
    public long RawActivationResponse { get; init; }
    public int? ActivationWin32Error { get; init; }
    public string? ImmediateEffectiveLayoutId { get; init; }
    public IReadOnlyList<LayoutVerificationSample> VerificationSamples { get; init; } = Array.Empty<LayoutVerificationSample>();
    public LayoutSwitchResult Result { get; init; } = LayoutSwitchResult.Failed;
    public string? FailureReason { get; init; }
    public bool RetryRecommended { get; init; }
}
