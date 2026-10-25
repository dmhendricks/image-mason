using ImageMason.Imaging;

namespace ImageMason.Tests.Imaging;

public sealed class JobMemoryGateTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reserve_WithinBudget_IsImmediate_AndReleaseFreesIt()
    {
        var gate = new JobMemoryGate(100);

        var first = await gate.ReserveAsync(60, Token);
        var second = await gate.ReserveAsync(40, Token);
        Assert.Equal(0, gate.AvailableBytes);

        first.Dispose();
        second.Dispose();
        Assert.Equal(100, gate.AvailableBytes);
    }

    [Fact]
    public async Task Reserve_LargerThanBudget_IsRejected()
    {
        var gate = new JobMemoryGate(100);

        var error = await Assert.ThrowsAsync<JobTooLargeException>(() => gate.ReserveAsync(101, Token));

        Assert.Equal(101, error.NeededBytes);
        Assert.Equal(100, gate.AvailableBytes);
    }

    [Fact]
    public async Task Reserve_WhenFull_WaitsUntilMemoryIsReleased()
    {
        var gate = new JobMemoryGate(100);
        var running = await gate.ReserveAsync(80, Token);

        var waiting = gate.ReserveAsync(50, Token);
        await Task.Delay(50, Token);
        Assert.False(waiting.IsCompleted);

        running.Dispose();
        using var admitted = await waiting.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.Equal(50, gate.AvailableBytes);
    }

    [Fact]
    public async Task Waiters_AreAdmittedInArrivalOrder_SoSmallJobsCannotStarveALargeOne()
    {
        var gate = new JobMemoryGate(100);
        var running = await gate.ReserveAsync(60, Token);

        var large = gate.ReserveAsync(90, Token);
        var small = gate.ReserveAsync(10, Token); // would fit now, but waits behind the large job
        await Task.Delay(50, Token);
        Assert.False(small.IsCompleted);

        running.Dispose();
        using var largeReservation = await large.WaitAsync(TimeSpan.FromSeconds(5), Token);
        using var smallReservation = await small.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.Equal(0, gate.AvailableBytes);
    }

    [Fact]
    public async Task CancellingAWaiter_RemovesIt_AndLetsTheNextOneIn()
    {
        var gate = new JobMemoryGate(100);
        using var running = await gate.ReserveAsync(60, Token);
        using var cancel = new CancellationTokenSource();

        var blocked = gate.ReserveAsync(90, cancel.Token);
        var next = gate.ReserveAsync(30, Token);

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);
        using var admitted = await next.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.Equal(10, gate.AvailableBytes);
    }

    [Fact]
    public async Task DisposingTwice_ReleasesOnce()
    {
        var gate = new JobMemoryGate(100);
        var reservation = await gate.ReserveAsync(40, Token);

        reservation.Dispose();
        reservation.Dispose();

        Assert.Equal(100, gate.AvailableBytes);
    }
}
