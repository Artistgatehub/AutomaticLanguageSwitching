using System.Text.Json.Serialization;

namespace AutomaticLanguageSwitching.NativeHost;

internal sealed class HostMessage
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("payload")]
    public MessagePayload Payload { get; init; } = new();
}

internal sealed class MessagePayload
{
    [JsonPropertyName("extensionVersion")]
    public string? ExtensionVersion { get; init; }

    [JsonPropertyName("hostVersion")]
    public string? HostVersion { get; init; }

    [JsonPropertyName("platform")]
    public string? Platform { get; init; }

    [JsonPropertyName("windowId")]
    public int? WindowId { get; init; }

    [JsonPropertyName("tabId")]
    public int? TabId { get; init; }

    [JsonPropertyName("previousWindowId")]
    public int? PreviousWindowId { get; init; }

    [JsonPropertyName("previousTabId")]
    public int? PreviousTabId { get; init; }

    [JsonPropertyName("currentWindowId")]
    public int? CurrentWindowId { get; init; }

    [JsonPropertyName("currentTabId")]
    public int? CurrentTabId { get; init; }

    [JsonPropertyName("layoutId")]
    public string? LayoutId { get; init; }

    [JsonPropertyName("result")]
    public string? Result { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
