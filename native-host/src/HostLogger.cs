using System.Globalization;
using System.Text;

namespace AutomaticLanguageSwitching.NativeHost;

internal static class HostLogger
{
    private static readonly object Sync = new();
    private static string? _logFilePath;
    private static int _fileLoggingFailureReported;

    public static string LogFilePath
    {
        get
        {
            EnsureInitialized();
            return _logFilePath!;
        }
    }

    public static void Initialize()
    {
        EnsureInitialized();
        Log($"[als-host] File logging initialized. path={_logFilePath}");
    }

    public static void Log(string message)
    {
        EnsureInitialized();

        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        var line = $"{timestamp} {message}";

        try
        {
            Console.Error.WriteLine(line);
        }
        catch
        {
            // Logging must never crash the host.
        }

        TryAppendLine(line);
    }

    private static void EnsureInitialized()
    {
        if (!string.IsNullOrWhiteSpace(_logFilePath))
        {
            return;
        }

        lock (Sync)
        {
            if (!string.IsNullOrWhiteSpace(_logFilePath))
            {
                return;
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var hostDirectory = Path.Combine(localAppData, "AutomaticLanguageSwitching", "NativeHost");
            _logFilePath = Path.Combine(hostDirectory, "als-native-host.log");

            try
            {
                Directory.CreateDirectory(hostDirectory);
            }
            catch
            {
                // File appends below safely handle directory creation failures too.
            }
        }
    }

    private static void TryAppendLine(string line)
    {
        if (string.IsNullOrWhiteSpace(_logFilePath))
        {
            return;
        }

        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath!)!);
                File.AppendAllText(_logFilePath!, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception exception)
        {
            if (Interlocked.Exchange(ref _fileLoggingFailureReported, 1) != 0)
            {
                return;
            }

            try
            {
                var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
                Console.Error.WriteLine(
                    $"{timestamp} [als-host] File logging failed path={_logFilePath} error={exception.GetType().Name}: {exception.Message}");
            }
            catch
            {
                // Suppress all logging failures.
            }
        }
    }
}
