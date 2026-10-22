using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using OpenSteps.App;

namespace OpenSteps.Tests;

public sealed class RecordingIslandLayoutTests
{
    [Fact]
    public void Island_RestoresNativeTopmostAfter120StepUpdatesWithoutTakingFocus()
    {
        OnSta(() =>
        {
            var island = new RecordingIslandWindow(() => Task.CompletedTask);
            var fixture = new Window { Width = 600, Height = 300, ShowInTaskbar = false };
            try
            {
                fixture.Show();
                island.Show();
                PumpWindowMessages();
                var handle = new WindowInteropHelper(island).Handle;
                for (var count = 1; count <= 120; count++)
                {
                    island.UpdateStatus(count, false);
                }

                // Simulate native z-order being changed while WPF's property stays true.
                Assert.True(SetWindowPos(handle, new IntPtr(-2), 0, 0, 0, 0, 0x0213));
                var foreground = GetForegroundWindow();
                island.EnsureTopmost();
                PumpWindowMessages();
                Assert.True(island.Topmost);
                Assert.NotEqual(0, GetWindowLong(handle, -20) & 0x00000008);
                Assert.Equal(foreground, GetForegroundWindow());
                Assert.Equal("120 steps", ((TextBlock)island.FindName("StepCountText")).Text);
            }
            finally
            {
                fixture.Close();
                island.Close();
            }
        });
    }

    [Fact]
    public void Island_RemainsTopmostAndHitTestableOverNormalAndMaximizedWindows()
    {
        OnSta(() =>
        {
            var island = new RecordingIslandWindow(() => Task.CompletedTask);
            var area = SystemParameters.WorkArea;
            var fixture = new Window
            {
                Title = "OpenSteps island regression fixture",
                Width = 600,
                Height = 300,
                Left = area.Left + Math.Max(0, (area.Width - 600) / 2),
                Top = area.Top,
                ShowInTaskbar = false
            };
            try
            {
                island.Show();
                PumpWindowMessages();
                var handle = new WindowInteropHelper(island).Handle;
                var button = (Button)island.FindName("StopButton");
                fixture.Show();

                foreach (var state in new[] { WindowState.Normal, WindowState.Maximized })
                {
                    fixture.WindowState = state;
                    fixture.Activate();
                    PumpWindowMessages();

                    Assert.NotEqual(0, GetWindowLong(handle, -20) & 0x00000008); // WS_EX_TOPMOST
                    Assert.Equal(0, GetWindowLong(handle, -16) & 0x00C00000); // WS_CAPTION
                    var center = button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
                    var hit = WindowFromPoint(new NativePoint { X = (int)center.X, Y = (int)center.Y });
                    Assert.Equal(handle, GetAncestor(hit, 2)); // GA_ROOT
                }
            }
            finally
            {
                fixture.Close();
                island.Close();
            }
        });
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(31, false)]
    [InlineData(31, true)]
    [InlineData(999, false)]
    public void Island_StopControlAndCountFitInsideBorderlessClient(int count, bool paused)
    {
        OnSta(() =>
        {
            var island = new RecordingIslandWindow(() => Task.CompletedTask);
            try
            {
                island.UpdateStatus(count, paused);
                var content = (FrameworkElement)island.Content;
                content.Measure(new System.Windows.Size(island.Width, island.Height));
                content.Arrange(new Rect(0, 0, island.Width, island.Height));
                content.UpdateLayout();

                var button = (Button)island.FindName("StopButton");
                var label = (TextBlock)island.FindName("StopLabel");
                var stepCount = (TextBlock)island.FindName("StepCountText");
                var buttonContent = (StackPanel)button.Content;
                var stopIcon = (FrameworkElement)buttonContent.Children[0];

                Assert.Equal(WindowStyle.None, island.WindowStyle);
                Assert.True(stopIcon.ActualWidth >= 38 && stopIcon.ActualHeight >= 38,
                    "The complete stop icon must retain its clickable visual size.");
                AssertFitsInside(content, button);
                AssertFitsInside(button, stopIcon);
                AssertFitsInside(button, label);
                AssertFitsInside(content, stepCount);
                AssertTextFits(label);
                AssertTextFits(stepCount);
                Assert.Equal($"{count} {(count == 1 ? "step" : "steps")}", stepCount.Text);

                var buttonBounds = button.TransformToAncestor(content)
                    .TransformBounds(new Rect(button.RenderSize));
                var countBounds = stepCount.TransformToAncestor(content)
                    .TransformBounds(new Rect(stepCount.RenderSize));
                Assert.True(buttonBounds.Right <= countBounds.Left,
                    "The step count must not overlap the stop control.");
            }
            finally
            {
                island.Close();
            }
        });
    }

    private static void AssertFitsInside(FrameworkElement parent, FrameworkElement child)
    {
        var bounds = child.TransformToAncestor(parent).TransformBounds(new Rect(child.RenderSize));
        var available = new Rect(-0.1, -0.1, parent.ActualWidth + 0.2, parent.ActualHeight + 0.2);
        Assert.True(bounds.Width > 0 && bounds.Height > 0 && available.Contains(bounds),
            $"{child.GetType().Name} bounds {bounds} exceed {parent.GetType().Name} bounds {available}.");
    }

    private static void AssertTextFits(TextBlock text)
    {
        var natural = new TextBlock
        {
            Text = text.Text,
            FontFamily = text.FontFamily,
            FontSize = text.FontSize,
            FontStyle = text.FontStyle,
            FontWeight = text.FontWeight,
            FontStretch = text.FontStretch
        };
        natural.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.True(text.ActualWidth + 0.1 >= natural.DesiredSize.Width
            && text.ActualHeight + 0.1 >= natural.DesiredSize.Height,
            $"Text '{text.Text}' is clipped within its allocated space.");
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

    private static void PumpWindowMessages()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr handle, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
