using OpenSteps.App;

namespace OpenSteps.Tests;

public sealed class RecordingWorkTrackerTests
{
    [Fact]
    public void Stop_CannotEnterTwice_AndCanBeUsedForNextRecording()
    {
        var work = new RecordingWorkTracker();
        Assert.True(work.TryBeginStop());
        Assert.False(work.TryBeginStop());
        work.CompleteStop();
        Assert.True(work.TryBeginStop());
    }

    [Fact]
    public async Task Drain_WaitsForLastClickAndKeyboardScreenshotBeforeExport()
    {
        var work = new RecordingWorkTracker();
        var click = new TaskCompletionSource();
        var keyboard = new TaskCompletionSource();
        var exportedSteps = new List<string>();
        var clickTask = work.TrackAsync(async () => { await click.Task; exportedSteps.Add("click"); });
        var keyboardTask = work.TrackAsync(async () => { await keyboard.Task; exportedSteps.Add("keyboard"); });
        Assert.True(work.TryBeginStop());
        var drain = work.DrainAsync();
        Assert.False(drain.IsCompleted);
        click.SetResult();
        await clickTask;
        Assert.False(drain.IsCompleted);
        keyboard.SetResult();
        await keyboardTask;
        await drain;
        Assert.Equal(new[] { "click", "keyboard" }, exportedSteps);
    }

    [Fact]
    public async Task FailedCapture_DoesNotRemainPendingForNextRecording()
    {
        var work = new RecordingWorkTracker();
        await Assert.ThrowsAsync<IOException>(() => work.TrackAsync(() => Task.FromException(new IOException("capture failed"))));
        await work.DrainAsync();
        var completed = false;
        await work.TrackAsync(() => { completed = true; return Task.CompletedTask; });
        await work.DrainAsync();
        Assert.True(completed);
    }
}
