using ImageMason.Endpoints;

namespace ImageMason.Tests.Endpoints;

public sealed class QueryCheckTests
{
    [Fact]
    public void Message_NamesTheUnknownParameters_AndWhatTheEndpointTakes()
    {
        Assert.Equal("Unknown parameter: rect. This endpoint takes url, filename.",
            QueryCheck.Message(["rect"], UploadEndpoint.Parameters));
        Assert.Equal("Unknown parameters: maxWidth, output. This endpoint takes url.",
            QueryCheck.Message(["maxWidth", "output"], ["url"]));
        Assert.Equal("Unknown parameter: t. This endpoint takes no parameters.",
            QueryCheck.Message(["t"], []));
    }
}
