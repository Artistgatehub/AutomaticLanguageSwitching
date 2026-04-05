using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace AutomaticLanguageSwitching.NativeHost.Tests;

public sealed class WindowsSmokeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    [Trait("Category", "WindowsSmoke")]
    public void KeyboardLayoutService_Can_Load_A_Real_Stable_Layout_Through_Win32()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var service = new KeyboardLayoutService();
        var targetLayoutId =
            service.GetCurrentLayoutSnapshot()?.StableRememberedLayoutId
            ?? service.GetConfiguredLayoutIds()
                .Select(service.TryGetStableLayoutIdForStorage)
                .FirstOrDefault(layoutId => layoutId is not null)
            ?? service.GetInstalledLayoutIds()
                .Select(service.TryGetStableLayoutIdForStorage)
                .FirstOrDefault(layoutId => layoutId is not null);

        targetLayoutId.Should().NotBeNull("the Windows profile should expose at least one stable layout");

        var loaded = service.TryLoadKeyboardLayoutForSmokeTest(
            targetLayoutId!,
            out var finalTargetLayoutId,
            out var loadResolution);

        loaded.Should().BeTrue();
        finalTargetLayoutId.Should().Be(targetLayoutId);
        loadResolution.Handle.Should().NotBe(IntPtr.Zero);
        loadResolution.CandidateKlid.Should().Be(finalTargetLayoutId);
        loadResolution.CanonicalHkl.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "WindowsSmoke")]
    public async Task NativeHost_Process_Accepts_Hello_And_Returns_HelloAck()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var hostAssemblyPath = typeof(NativeMessagingHost).Assembly.Location;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{hostAssemblyPath}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start().Should().BeTrue();

        await WriteNativeMessageAsync(
            process.StandardInput.BaseStream,
            new HostMessage
            {
                Version = 1,
                Type = "hello",
                Payload = new MessagePayload
                {
                    ExtensionVersion = "0.2.1"
                }
            });

        process.StandardInput.Close();

        var firstMessage = await ReadNativeMessageAsync(process.StandardOutput.BaseStream);
        firstMessage.Should().NotBeNull();
        firstMessage!.Type.Should().Be("hello_ack");
        firstMessage.Payload.HostVersion.Should().Be("0.2.1");
        firstMessage.Payload.Platform.Should().Be("windows");

        var exited = process.WaitForExit(5000);
        exited.Should().BeTrue(process.StandardError.ReadToEnd());
        process.ExitCode.Should().Be(0);
    }

    private static async Task WriteNativeMessageAsync(Stream stream, HostMessage message)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var length = BitConverter.GetBytes(payload.Length);

        await stream.WriteAsync(length);
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
    }

    private static async Task<HostMessage?> ReadNativeMessageAsync(Stream stream)
    {
        var lengthBuffer = new byte[4];
        var headerRead = await ReadExactlyAsync(stream, lengthBuffer);
        if (headerRead == 0)
        {
            return null;
        }

        var payloadLength = BitConverter.ToInt32(lengthBuffer, 0);
        var payloadBuffer = new byte[payloadLength];
        await ReadExactlyAsync(stream, payloadBuffer);
        return JsonSerializer.Deserialize<HostMessage>(payloadBuffer, JsonOptions);
    }

    private static async Task<int> ReadExactlyAsync(Stream stream, byte[] buffer)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead));
            if (bytesRead == 0)
            {
                return totalRead;
            }

            totalRead += bytesRead;
        }

        return totalRead;
    }
}
