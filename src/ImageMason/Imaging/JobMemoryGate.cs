using ImageMason.Configuration;

namespace ImageMason.Imaging;

/// <summary>An optimization job needs more memory than the service has for jobs, so it can never run.</summary>
public sealed class JobTooLargeException(long neededBytes, long budgetBytes) : Exception(
    $"Optimizing this image needs about {ByteSizes.ToGigabytes(neededBytes)} of memory, but this server has " +
    $"{ByteSizes.ToGigabytes(budgetBytes)} for jobs. Send a smaller image, pass width, or use quality=lossless.")
{
    public long NeededBytes { get; } = neededBytes;
    public long BudgetBytes { get; } = budgetBytes;
}

/// <summary>
/// Admits optimization jobs by their estimated peak memory, so the jobs running at once never add up to more than
/// the budget. A job that doesn't fit yet waits; waiters are admitted in arrival order, so a large job is not starved
/// by a stream of small ones. A job larger than the whole budget is rejected.
/// </summary>
public sealed class JobMemoryGate(long budgetBytes)
{
    private readonly Lock _lock = new();
    private readonly LinkedList<Waiter> _waiters = new();
    private long _available = budgetBytes;

    public long BudgetBytes { get; } = budgetBytes;

    /// <summary>
    /// The container's memory limit from its cgroup, or the machine's memory when there is none. Read directly
    /// because the runtime's own figure is its GC heap limit, which defaults to 75% of the container's limit.
    /// </summary>
    public static long DetectMemoryLimit()
    {
        // cgroup v2, then v1. v2 reports "max" and v1 a huge number when unlimited.
        foreach (var path in (string[])["/sys/fs/cgroup/memory.max", "/sys/fs/cgroup/memory/memory.limit_in_bytes"])
        {
            try
            {
                if (File.Exists(path) && long.TryParse(File.ReadAllText(path).Trim(), out var limit) && limit is > 0 and < long.MaxValue / 2)
                    return limit;
            }
            catch (IOException)
            {
                // Unreadable: try the next source.
            }
        }

        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }

    /// <summary>Bytes not reserved by running jobs.</summary>
    public long AvailableBytes
    {
        get
        {
            lock (_lock)
                return _available;
        }
    }

    /// <summary>Waits until <paramref name="bytes"/> are free and reserves them. Dispose the result to release them.</summary>
    /// <exception cref="JobTooLargeException"><paramref name="bytes"/> exceeds the whole budget.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled while waiting.</exception>
    public async Task<IDisposable> ReserveAsync(long bytes, CancellationToken cancellationToken)
    {
        if (bytes > BudgetBytes)
            throw new JobTooLargeException(bytes, BudgetBytes);

        Waiter waiter;
        lock (_lock)
        {
            if (_waiters.Count == 0 && bytes <= _available)
            {
                _available -= bytes;
                return new Reservation(this, bytes);
            }

            waiter = new Waiter(bytes);
            waiter.Node = _waiters.AddLast(waiter);
        }

        await using (cancellationToken.Register(() => Cancel(waiter)))
            await waiter.Admitted.Task;

        return new Reservation(this, bytes);
    }

    private void Cancel(Waiter waiter)
    {
        List<Waiter> admitted;
        lock (_lock)
        {
            // Already admitted: the reservation stands and the caller releases it.
            if (waiter.Node?.List is null)
                return;

            _waiters.Remove(waiter.Node);
            admitted = AdmitWaiting();
        }

        waiter.Admitted.TrySetCanceled();
        Complete(admitted);
    }

    private void Release(long bytes)
    {
        List<Waiter> admitted;
        lock (_lock)
        {
            _available += bytes;
            admitted = AdmitWaiting();
        }

        Complete(admitted);
    }

    // Caller holds _lock. Admits waiters from the front of the line while they fit.
    private List<Waiter> AdmitWaiting()
    {
        var admitted = new List<Waiter>();
        while (_waiters.First is { } next && next.Value.Bytes <= _available)
        {
            _available -= next.Value.Bytes;
            _waiters.RemoveFirst();
            admitted.Add(next.Value);
        }

        return admitted;
    }

    // Completed outside the lock, so continuations never run while it is held.
    private static void Complete(List<Waiter> admitted)
    {
        foreach (var waiter in admitted)
            waiter.Admitted.TrySetResult();
    }

    private sealed class Waiter(long bytes)
    {
        public long Bytes { get; } = bytes;
        public TaskCompletionSource Admitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LinkedListNode<Waiter>? Node { get; set; }
    }

    private sealed class Reservation(JobMemoryGate gate, long bytes) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                gate.Release(bytes);
        }
    }
}
