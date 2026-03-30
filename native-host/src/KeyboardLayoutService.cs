using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AutomaticLanguageSwitching.NativeHost;

internal sealed class KeyboardLayoutService
{
    private const string KeyboardLayoutRegistryPath = @"Keyboard Layout\Preload";
    private const string KeyboardLayoutSubstitutesRegistryPath = @"Keyboard Layout\Substitutes";
    private static readonly TimeSpan LayoutActivationWaitTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan LayoutActivationPollInterval = TimeSpan.FromMilliseconds(25);

    private const uint KlfActivate = 0x00000001;
    private const uint KlfSubstituteOk = 0x00000002;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint WmInputLangChangeRequest = 0x0050;

    private static readonly TimeSpan InstalledLayoutsCacheTtl = TimeSpan.FromSeconds(5);
    private static readonly Regex LayoutIdPattern = new("^[0-9A-F]{8}$", RegexOptions.Compiled);

    private readonly object _cacheLock = new();
    private IReadOnlyCollection<string>? _installedLayoutIdsCache;
    private DateTimeOffset _installedLayoutIdsCachedAt = DateTimeOffset.MinValue;

    public IReadOnlyCollection<string> GetInstalledLayoutIds()
    {
        lock (_cacheLock)
        {
            if (_installedLayoutIdsCache is not null &&
                DateTimeOffset.UtcNow - _installedLayoutIdsCachedAt < InstalledLayoutsCacheTtl)
            {
                Console.Error.WriteLine(
                    $"[als-host] Installed layouts cache (cached): {string.Join(", ", _installedLayoutIdsCache)}");
                return _installedLayoutIdsCache;
            }

            _installedLayoutIdsCache = ReadInstalledLayoutIds();
            _installedLayoutIdsCachedAt = DateTimeOffset.UtcNow;

            Console.Error.WriteLine(
                $"[als-host] Installed layouts cache (fresh): {string.Join(", ", _installedLayoutIdsCache)}");

            return _installedLayoutIdsCache;
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

    public string? GetCurrentLayoutId()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            Console.Error.WriteLine("[als-host] GetCurrentLayoutId failed: GetForegroundWindow returned zero.");
            return null;
        }

        var threadId = GetWindowThreadProcessId(foregroundWindow, out _);
        if (threadId == 0)
        {
            Console.Error.WriteLine("[als-host] GetCurrentLayoutId failed: GetWindowThreadProcessId returned zero.");
            return null;
        }

        var keyboardLayout = GetKeyboardLayout(threadId);
        if (keyboardLayout == IntPtr.Zero)
        {
            Console.Error.WriteLine("[als-host] GetCurrentLayoutId failed: GetKeyboardLayout returned zero.");
            return null;
        }

        var layoutId = (keyboardLayout.ToInt64() & 0xFFFFFFFFL).ToString("X8");
        Console.Error.WriteLine($"[als-host] Current foreground layoutId={layoutId}");
        return layoutId;
    }

    public LayoutSwitchResult TrySwitchTo(string layoutId)
    {
        var normalized = NormalizeLayoutId(layoutId);
        if (normalized is null)
        {
            Console.Error.WriteLine("[als-host] TrySwitchTo failed: invalid layoutId.");
            return LayoutSwitchResult.Failed;
        }

        Console.Error.WriteLine($"[als-host] TrySwitchTo requested layoutId={normalized}");

        var visibleLayouts = GetInstalledLayoutIds();
        Console.Error.WriteLine($"[als-host] Visible layouts before switch: {string.Join(", ", visibleLayouts)}");

        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            Console.Error.WriteLine("[als-host] TrySwitchTo failed: GetForegroundWindow returned zero.");
            return LayoutSwitchResult.Failed;
        }

        if (!IsChromeForegroundWindow(foregroundWindow))
        {
            Console.Error.WriteLine("[als-host] TrySwitchTo failed: foreground window is not chrome.exe.");
            return LayoutSwitchResult.Failed;
        }

        if (!TryLoadKeyboardLayoutHandle(normalized, out var loadedLayout))
        {
            return LayoutSwitchResult.Unavailable;
        }

        var messageResult = SendMessageTimeout(
            foregroundWindow,
            WmInputLangChangeRequest,
            IntPtr.Zero,
            loadedLayout,
            SmtoAbortIfHung,
            1000,
            out _);

        if (messageResult == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Console.Error.WriteLine(
                $"[als-host] SendMessageTimeout failed for {normalized}. GetLastWin32Error={error}");
            return LayoutSwitchResult.Failed;
        }

        Console.Error.WriteLine($"[als-host] SendMessageTimeout succeeded for {normalized}.");
        WaitForLayoutActivation(normalized);
        return LayoutSwitchResult.Applied;
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

    private bool TryLoadKeyboardLayoutHandle(string targetLayoutId, out IntPtr loadedLayout)
    {
        foreach (var candidateKlid in GetLoadKeyboardLayoutCandidates(targetLayoutId))
        {
            var candidateHandle = LoadKeyboardLayout(candidateKlid, KlfActivate | KlfSubstituteOk);
            if (candidateHandle == IntPtr.Zero)
            {
                var loadError = Marshal.GetLastWin32Error();
                Console.Error.WriteLine(
                    $"[als-host] LoadKeyboardLayout failed for candidate {candidateKlid} while targeting {targetLayoutId}. GetLastWin32Error={loadError}");
                continue;
            }

            var candidateLayoutId = NormalizeHkl(candidateHandle);
            Console.Error.WriteLine(
                $"[als-host] LoadKeyboardLayout candidate {candidateKlid} produced HKL=0x{candidateHandle.ToInt64():X} normalized={candidateLayoutId ?? "null"}");

            if (string.Equals(candidateLayoutId, targetLayoutId, StringComparison.OrdinalIgnoreCase))
            {
                loadedLayout = candidateHandle;
                return true;
            }
        }

        Console.Error.WriteLine(
            $"[als-host] Could not load a keyboard layout handle matching target {targetLayoutId}.");
        loadedLayout = IntPtr.Zero;
        return false;
    }

    private static IEnumerable<string> GetLoadKeyboardLayoutCandidates(string targetLayoutId)
    {
        yield return targetLayoutId;

        if (targetLayoutId.StartsWith(targetLayoutId[4..], StringComparison.OrdinalIgnoreCase))
        {
            yield return $"0000{targetLayoutId[4..]}";
        }
    }

    private static string? NormalizeHkl(IntPtr keyboardLayout)
    {
        if (keyboardLayout == IntPtr.Zero)
        {
            return null;
        }

        return NormalizeLayoutId((keyboardLayout.ToInt64() & 0xFFFFFFFFL).ToString("X8"));
    }

    private void WaitForLayoutActivation(string expectedLayoutId)
    {
        var deadline = DateTimeOffset.UtcNow + LayoutActivationWaitTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var currentLayoutId = GetCurrentLayoutId();
            if (string.Equals(currentLayoutId, expectedLayoutId, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(
                    $"[als-host] Observed active layout '{expectedLayoutId}' after restore request.");
                return;
            }

            Thread.Sleep(LayoutActivationPollInterval);
        }

        Console.Error.WriteLine(
            $"[als-host] Timed out waiting for layout '{expectedLayoutId}' to become active after restore request.");
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);
}

internal enum LayoutSwitchResult
{
    Applied,
    Unavailable,
    Failed
}
