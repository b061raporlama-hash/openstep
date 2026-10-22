namespace OpenSteps.App;

// Accessed only on the UI dispatcher, including asynchronous continuations.
internal sealed class RecordingWorkTracker
{
    private readonly HashSet<Task> _pending = [];

    internal bool IsStopping { get; private set; }

    internal bool TryBeginStop()
    {
        if (IsStopping)
        {
            return false;
        }

        IsStopping = true;
        return true;
    }

    internal void CompleteStop() => IsStopping = false;

    internal async Task TrackAsync(Func<Task> operation)
    {
        var task = operation();
        _pending.Add(task);
        try
        {
            await task;
        }
        finally
        {
            _pending.Remove(task);
        }
    }

    internal async Task DrainAsync()
    {
        while (_pending.Count > 0)
        {
            await Task.WhenAll(_pending.ToArray());
        }
    }
}
