namespace AutomaticLanguageSwitching.NativeHost;

internal static class Program
{
    private static async Task Main()
    {
        try
        {
            Console.Error.WriteLine("[als-host] Startup.");
            using var input = Console.OpenStandardInput();
            using var output = Console.OpenStandardOutput();

            var host = new NativeMessagingHost(input, output);
            await host.RunAsync(CancellationToken.None);
            Console.Error.WriteLine("[als-host] Shutdown.");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[als-host] Fatal error: {exception}");
            Environment.ExitCode = 1;
        }
    }
}
