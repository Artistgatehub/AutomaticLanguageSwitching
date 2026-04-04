namespace AutomaticLanguageSwitching.NativeHost;

internal static class Program
{
    private static async Task Main()
    {
        try
        {
            HostLogger.Initialize();
            HostLogger.Log(
                $"[als-host] Startup. exe={Environment.ProcessPath ?? "unknown"} baseDir={AppContext.BaseDirectory} cwd={Environment.CurrentDirectory}");
            using var input = Console.OpenStandardInput();
            using var output = Console.OpenStandardOutput();

            var host = new NativeMessagingHost(input, output);
            await host.RunAsync(CancellationToken.None);
            HostLogger.Log("[als-host] Shutdown.");
        }
        catch (Exception exception)
        {
            HostLogger.Log($"[als-host] Fatal error: {exception}");
            Environment.ExitCode = 1;
        }
    }
}
