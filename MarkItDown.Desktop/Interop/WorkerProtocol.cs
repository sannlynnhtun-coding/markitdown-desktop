using System.Text.Json.Serialization;

namespace MarkItDown.Desktop.Interop;

public static class WorkerProtocol
{
    public const int Version = 1;
}

public sealed record WorkerRequest(
    [property: JsonPropertyName("protocolVersion")] int ProtocolVersion,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("inputPath")] string InputPath,
    [property: JsonPropertyName("cacheDirectory")] string CacheDirectory);

public sealed record WorkerResponse
{
    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; init; }

    [JsonPropertyName("event")]
    public string Event { get; init; } = string.Empty;

    [JsonPropertyName("requestId")]
    public string? RequestId { get; init; }

    [JsonPropertyName("markdownPath")]
    public string? MarkdownPath { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("elapsedMs")]
    public long? ElapsedMs { get; init; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

public sealed record WorkerConversionResult(string MarkdownPath, string? Title, long ElapsedMilliseconds);
