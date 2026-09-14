using System.Text.Json;
using MarkItDown.Desktop.Interop;

namespace MarkItDown.Desktop.Tests;

public sealed class WorkerProtocolTests
{
    [Test]
    public void CompletedResponse_DeserializesStableWireNames()
    {
        const string json = """
            {"protocolVersion":1,"event":"completed","requestId":"42","markdownPath":"C:\\cache\\42.md","title":"Report","elapsedMs":125}
            """;

        var response = JsonSerializer.Deserialize<WorkerResponse>(json);

        response.Should().NotBeNull();
        response!.ProtocolVersion.Should().Be(WorkerProtocol.Version);
        response.Event.Should().Be("completed");
        response.MarkdownPath.Should().EndWith("42.md");
        response.ElapsedMs.Should().Be(125);
    }

    [Test]
    public void ErrorResponse_DeserializesStructuredErrorWithoutDocumentContent()
    {
        const string json = """
            {"protocolVersion":1,"event":"error","requestId":"42","errorCode":"conversion_failed","message":"The document could not be converted."}
            """;

        var response = JsonSerializer.Deserialize<WorkerResponse>(json);

        response.Should().NotBeNull();
        response!.Event.Should().Be("error");
        response.ErrorCode.Should().Be("conversion_failed");
        response.MarkdownPath.Should().BeNull();
        json.Should().NotContain("inputPath");
    }
}
