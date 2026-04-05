using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace AutomaticLanguageSwitching.NativeHost.Tests;

public sealed class NativeMessagingHostTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task Hello_Returns_HelloAck_For_Current_Protocol()
    {
        var fakeService = new FakeKeyboardLayoutService();
        await using var input = CreateInputStream(
            CreateMessage("hello", new MessagePayload { ExtensionVersion = "0.2.1" }));
        await using var output = new MemoryStream();

        var host = new NativeMessagingHost(
            input,
            output,
            fakeService,
            new PerAppInputMethodStatus(true, false));

        await host.RunAsync(CancellationToken.None);

        var messages = ReadOutputMessages(output);
        messages.Should().ContainSingle();
        messages[0].Type.Should().Be("hello_ack");
        messages[0].Payload.HostVersion.Should().Be("0.2.1");
    }

    [Fact]
    public async Task TabSwitched_With_Missing_Current_Tab_Returns_Error()
    {
        var fakeService = new FakeKeyboardLayoutService();
        await using var input = CreateInputStream(
            CreateMessage("tab_switched", new MessagePayload()));
        await using var output = new MemoryStream();

        var host = new NativeMessagingHost(
            input,
            output,
            fakeService,
            new PerAppInputMethodStatus(true, false));

        await host.RunAsync(CancellationToken.None);

        var messages = ReadOutputMessages(output);
        messages.Should().ContainSingle();
        messages[0].Type.Should().Be("error");
        messages[0].Payload.Message.Should().Contain("tab_switched requires currentWindowId and currentTabId");
    }

    [Fact]
    public async Task ChromeFocusReturned_Sends_Normalized_RestoreResult_LayoutId()
    {
        var fakeService = new FakeKeyboardLayoutService
        {
            CurrentLayoutId = "00000422",
            StableLayoutIdForStorage = _ => "00000409",
            SwitchResults = new Queue<LayoutSwitchAttemptResult>(
            [
                new LayoutSwitchAttemptResult
                {
                    Result = LayoutSwitchResult.Applied,
                    AttemptNumber = 1,
                    AttemptDelayMs = 0,
                    RequestedLayoutId = "04090409",
                    CanonicalRequestedLayoutId = "00000409"
                }
            ])
        };

        await using var input = CreateInputStream(
            CreateMessage("chrome_focus_returned", new MessagePayload
            {
                CurrentWindowId = 1,
                CurrentTabId = 1
            }));
        await using var output = new MemoryStream();

        var host = new NativeMessagingHost(
            input,
            output,
            fakeService,
            new PerAppInputMethodStatus(true, false));

        SetCurrentActiveTab(host, new TabKey(1, 1));
        SetRememberedLayout(host, new TabKey(1, 1), "04090409");

        await host.RunAsync(CancellationToken.None);

        var messages = ReadOutputMessages(output);
        messages.Should().ContainSingle();
        messages[0].Type.Should().Be("layout_restore_result");
        messages[0].Payload.LayoutId.Should().Be("00000409");
        messages[0].Payload.LayoutId.Should().NotBe("04090409");
    }

    [Fact]
    public async Task FailedRestore_Arms_OneShot_Protection_And_Preserves_Previous_Stored_Layout()
    {
        var fakeService = new FakeKeyboardLayoutService
        {
            CurrentSnapshots = new Queue<ObservedLayoutSnapshot?>(
            [
                new ObservedLayoutSnapshot((IntPtr)1, 1, "04090409", "00000409", "00000409", "00000409", "test"),
                new ObservedLayoutSnapshot((IntPtr)1, 1, "04090409", "00000409", "00000409", "00000409", "test")
            ]),
            StableLayoutIdForStorage = layoutId => layoutId switch
            {
                "00000422" => "00000422",
                "00000409" => "00000409",
                _ => KeyboardLayoutRules.TryNormalizeToStrictStableKlid(layoutId)
            },
            SwitchResults = new Queue<LayoutSwitchAttemptResult>(
            [
                new LayoutSwitchAttemptResult
                {
                    Result = LayoutSwitchResult.Failed,
                    AttemptNumber = 1,
                    AttemptDelayMs = 0,
                    RetryRecommended = false,
                    CanonicalRequestedLayoutId = "00000422",
                    RequestedLayoutId = "00000422"
                }
            ])
        };

        await using var input = CreateInputStream(
            CreateMessage("tab_switched", new MessagePayload
            {
                PreviousWindowId = 2,
                PreviousTabId = 2,
                CurrentWindowId = 1,
                CurrentTabId = 1
            }),
            CreateMessage("tab_switched", new MessagePayload
            {
                PreviousWindowId = 1,
                PreviousTabId = 1,
                CurrentWindowId = 3,
                CurrentTabId = 3
            }));
        await using var output = new MemoryStream();

        var host = new NativeMessagingHost(
            input,
            output,
            fakeService,
            new PerAppInputMethodStatus(true, false));

        SetCurrentActiveTab(host, new TabKey(2, 2));
        SetRememberedLayout(host, new TabKey(1, 1), "00000422");
        SetRememberedLayout(host, new TabKey(2, 2), "00000409");

        await host.RunAsync(CancellationToken.None);

        GetRememberedLayouts(host)[new TabKey(1, 1)].Should().Be("00000422");

        var messages = ReadOutputMessages(output);
        messages.Should().ContainSingle(message =>
            message.Type == "layout_restore_result" &&
            message.Payload.TabId == 1 &&
            message.Payload.LayoutId == "00000422" &&
            message.Payload.Result == "failed");
    }

    [Fact]
    public async Task TabClosed_Clears_Remembered_Layout()
    {
        var fakeService = new FakeKeyboardLayoutService();
        await using var input = CreateInputStream(
            CreateMessage("tab_closed", new MessagePayload
            {
                WindowId = 5,
                TabId = 9
            }));
        await using var output = new MemoryStream();

        var host = new NativeMessagingHost(
            input,
            output,
            fakeService,
            new PerAppInputMethodStatus(true, false));

        SetRememberedLayout(host, new TabKey(5, 9), "00000409");

        await host.RunAsync(CancellationToken.None);

        GetRememberedLayouts(host).Should().NotContainKey(new TabKey(5, 9));
        ReadOutputMessages(output).Should().BeEmpty();
    }

    private static HostMessage CreateMessage(string type, MessagePayload payload)
    {
        return new HostMessage
        {
            Version = 1,
            Type = type,
            Payload = payload
        };
    }

    private static MemoryStream CreateInputStream(params HostMessage[] messages)
    {
        var stream = new MemoryStream();
        foreach (var message in messages)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
            var length = BitConverter.GetBytes(payload.Length);
            stream.Write(length);
            stream.Write(payload);
        }

        stream.Position = 0;
        return stream;
    }

    private static List<HostMessage> ReadOutputMessages(MemoryStream output)
    {
        output.Position = 0;
        var messages = new List<HostMessage>();

        while (output.Position < output.Length)
        {
            var lengthBuffer = new byte[4];
            output.ReadExactly(lengthBuffer);
            var payloadLength = BitConverter.ToInt32(lengthBuffer, 0);
            var payloadBuffer = new byte[payloadLength];
            output.ReadExactly(payloadBuffer);
            messages.Add(JsonSerializer.Deserialize<HostMessage>(payloadBuffer, JsonOptions)!);
        }

        return messages;
    }

    private static void SetRememberedLayout(NativeMessagingHost host, TabKey key, string layoutId)
    {
        GetRememberedLayouts(host)[key] = layoutId;
    }

    private static Dictionary<TabKey, string> GetRememberedLayouts(NativeMessagingHost host)
    {
        return (Dictionary<TabKey, string>)typeof(NativeMessagingHost)
            .GetField("_rememberedLayouts", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(host)!;
    }

    private static void SetCurrentActiveTab(NativeMessagingHost host, TabKey key)
    {
        typeof(NativeMessagingHost)
            .GetField("_currentActiveTab", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(host, key);
    }

    private sealed class FakeKeyboardLayoutService : IKeyboardLayoutService
    {
        public Queue<ObservedLayoutSnapshot?> CurrentSnapshots { get; init; } = [];
        public string? CurrentLayoutId { get; init; }
        public Func<string, string?> StableLayoutIdForStorage { get; init; } = layoutId => layoutId;
        public Queue<LayoutSwitchAttemptResult> SwitchResults { get; init; } = [];

        public bool? IsPerAppInputMethodEnabled() => true;

        public bool TryEnablePerAppInputMethod() => true;

        public string? GetCurrentLayoutId() => CurrentLayoutId;

        public ObservedLayoutSnapshot? GetCurrentLayoutSnapshot()
        {
            return CurrentSnapshots.Count > 0
                ? CurrentSnapshots.Dequeue()
                : null;
        }

        public string? TryGetStableLayoutIdForStorage(string layoutId) => StableLayoutIdForStorage(layoutId);

        public LayoutSwitchAttemptResult TrySwitchTo(string layoutId, RestoreAttemptContext context)
        {
            return SwitchResults.Count > 0
                ? SwitchResults.Dequeue() with { StoredLayoutId = layoutId }
                : new LayoutSwitchAttemptResult
                {
                    Result = LayoutSwitchResult.Applied,
                    AttemptNumber = context.AttemptNumber,
                    AttemptDelayMs = context.AttemptDelayMs,
                    StoredLayoutId = layoutId,
                    RequestedLayoutId = layoutId,
                    CanonicalRequestedLayoutId = layoutId
                };
        }
    }
}
