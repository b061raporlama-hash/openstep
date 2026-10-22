using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;

namespace OpenSteps.Tests;

public sealed class ControlStyleTests
{
    [Fact]
    public void CaptureControls_DisplayModeNamesAndWrapDescriptions()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
                var source = XDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "App.xaml"));
                var dictionary = new XElement(presentation + "ResourceDictionary",
                    new XAttribute(XNamespace.Xmlns + "x", xaml),
                    source.Root!.Element(presentation + "Application.Resources")!.Elements()
                        .Where(element => element.Name == presentation + "SolidColorBrush"
                            || element.Attribute("TargetType")?.Value is "ComboBox" or "ComboBoxItem" or "CheckBox"));
                var resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
                var combo = new ComboBox
                {
                    Resources = resources,
                    Style = (Style)resources[typeof(ComboBox)],
                    DisplayMemberPath = "DisplayName",
                    ItemsSource = new[]
                    {
                        new { DisplayName = "Full Desktop" },
                        new { DisplayName = "Active Window" }
                    }
                };

                foreach (var (index, label) in new[] { (0, "Full Desktop"), (1, "Active Window") })
                {
                    combo.SelectedIndex = index;
                    combo.Measure(new System.Windows.Size(200, 40));
                    combo.Arrange(new Rect(0, 0, 200, 40));
                    combo.UpdateLayout();

                    Assert.Contains(Descendants(combo).OfType<TextBlock>(), text => text.Text == label);
                }

                var description = new TextBlock
                {
                    Text = "One offline .html file with embedded screenshots. No accompanying files needed.",
                    TextWrapping = TextWrapping.Wrap
                };
                var checkBox = new CheckBox { Resources = resources, Content = description };
                checkBox.Style = (Style)resources[typeof(CheckBox)];
                checkBox.Measure(new System.Windows.Size(200, double.PositiveInfinity));
                checkBox.Arrange(new Rect(0, 0, 200, checkBox.DesiredSize.Height));
                checkBox.UpdateLayout();

                Assert.True(description.ActualWidth <= 172, "Option description exceeds available width.");
                Assert.True(description.ActualHeight > 30, "Long option description should wrap.");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF style test timed out.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
