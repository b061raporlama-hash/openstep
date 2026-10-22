using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using OpenSteps.App;

namespace OpenSteps.Tests;

public sealed class RecordingIslandWindowTests
{
    [Theory]
    [InlineData(0, false, "0 steps", "Stop Recording")]
    [InlineData(1, false, "1 step", "Stop Recording")]
    [InlineData(31, false, "31 steps", "Stop Recording")]
    [InlineData(31, true, "31 steps", "Paused · Stop")]
    public void Island_DisplaysCurrentCountAndPauseState(int count, bool paused, string expectedCount, string expectedLabel)
    {
        OnSta(() =>
        {
            var island = new RecordingIslandWindow(() => Task.CompletedTask);
            try
            {
                // Test the application's compiled BAML, not a separately compiled test copy.
                Assert.NotEqual(typeof(RecordingIslandWindowTests).Assembly, island.GetType().Assembly);
                island.UpdateStatus(count, paused);
                Assert.Equal(expectedCount, ((TextBlock)island.FindName("StepCountText")).Text);
                Assert.Equal(expectedLabel, ((TextBlock)island.FindName("StopLabel")).Text);
                Assert.True(island.Topmost);
                Assert.False(island.ShowInTaskbar);
                Assert.False(island.ShowActivated);
                Assert.Equal(WindowStyle.None, island.WindowStyle);
                Assert.True(island.AllowsTransparency);
            }
            finally
            {
                island.Close();
            }
        });
    }

    [Fact]
    public void StopButton_DisablesItselfAndInvokesStopOperation()
    {
        OnSta(() =>
        {
            var calls = 0;
            var island = new RecordingIslandWindow(() => { calls++; return Task.CompletedTask; });
            try
            {
                var button = (Button)island.FindName("StopButton");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, calls);
                Assert.False(button.IsEnabled);
            }
            finally
            {
                island.Close();
            }
        });
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
