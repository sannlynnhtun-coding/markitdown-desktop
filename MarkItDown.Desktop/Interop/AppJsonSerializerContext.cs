using System.Text.Json.Serialization;

namespace MarkItDown.Desktop.Interop;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(WorkerRequest))]
[JsonSerializable(typeof(WorkerResponse))]
[JsonSerializable(typeof(LogEntry))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext;

internal sealed record LogEntry(
    [property: JsonPropertyName("timestampUtc")] DateTimeOffset TimestampUtc,
    [property: JsonPropertyName("level")] string Level,
    [property: JsonPropertyName("eventName")] string EventName,
    [property: JsonPropertyName("requestId")] Guid? RequestId,
    [property: JsonPropertyName("extension")] string? Extension,
    [property: JsonPropertyName("elapsedMs")] long? ElapsedMs,
    [property: JsonPropertyName("errorCode")] string? ErrorCode);
