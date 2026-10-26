using ImageMason.Imaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageMason.Tests.Imaging;

public sealed class ToolRunnerTests
{
    private readonly ToolRunner _tools = new(NullLogger<ToolRunner>.Instance);

    [Fact]
    public async Task Run_ReturnsExitCodeAndOutput()
    {
        var result = await _tools.RunAsync("sh", ["-c", "echo out; echo err >&2; exit 3"], TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("out", result.StdOut.Trim());
        Assert.Equal("err", result.StdErr.Trim());
    }

    [Fact]
    public async Task ToolKilledFromOutside_Throws_InsteadOfReturningAFailedResult()
    {
        var error = await Assert.ThrowsAsync<ToolKilledException>(
            () => _tools.RunAsync("sh", ["-c", "kill -9 $$"], TestContext.Current.CancellationToken));

        Assert.Equal("sh", error.Tool);
    }

    [Fact]
    public async Task Cancellation_KillsTheTool_AndThrowsCancellation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _tools.RunAsync("sleep", ["10"], timeout.Token));
    }

    [Fact]
    public async Task Output_IsCapped_ButFullyRead()
    {
        var result = await _tools.RunAsync("sh", ["-c", "head -c 200000 /dev/zero | tr '\\0' a; echo done >&2"],
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(new string('a', ToolRunner.MaxOutputChars) + " [truncated]", result.StdOut);
        Assert.Equal("done", result.StdErr.Trim());
    }
}
