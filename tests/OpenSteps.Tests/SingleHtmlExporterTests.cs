using System.Text.RegularExpressions;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.Tests;

public sealed class SingleHtmlExporterTests : IDisposable
{
    private const string OriginalPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==";
    private const string EditedPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGD4DwABBAEAX+XDSwAAAABJRU5ErkJggg==";
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "OpenSteps.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SingleHtml_EmbedsExactImageBytesAndCreatesOnlyOneFile()
    {
        var image = await WriteImageAsync("capture.png", OriginalPng);
        var session = new RecordingSession { Title = "Single HTML" };
        session.Steps.Add(new RecordedStep { UserTitle = "Capture", ScreenshotPath = image });

        var result = await new MarkdownExporter().ExportAsync(session, _temp, GuideExportFormat.SingleHtml);
        var html = await File.ReadAllTextAsync(result.SingleHtmlPath);

        Assert.Equal(result.SingleHtmlPath, Assert.Single(result.OutputPaths));
        Assert.Equal(result.SingleHtmlPath, Assert.Single(Directory.GetFileSystemEntries(result.ExportFolder)));
        Assert.Empty(result.MarkdownPath);
        Assert.Empty(result.HtmlPath);
        Assert.Empty(result.Warnings);
        Assert.Equal(await File.ReadAllBytesAsync(image), Assert.Single(ReadEmbeddedImages(html)));
        Assert.Contains("<style>", html);

        // The report must remain self-contained after the source and export folder are gone.
        var portablePath = Path.Combine(_temp, "portable.html");
        File.Move(result.SingleHtmlPath, portablePath);
        Directory.Delete(result.ExportFolder);
        File.Delete(image);
        var portableHtml = await File.ReadAllTextAsync(portablePath);
        Assert.Equal(html, portableHtml);
        Assert.Equal(Convert.FromBase64String(OriginalPng), Assert.Single(ReadEmbeddedImages(portableHtml)));
        Assert.DoesNotContain("images/", portableHtml);
        Assert.DoesNotContain(image, portableHtml);
        Assert.DoesNotMatch("(?i)<(?:script|link|iframe|object|embed)\\b|@import|url\\(", portableHtml);
        var references = Regex.Matches(portableHtml, "(?:src|href)=[\"']([^\"']+)[\"']");
        Assert.StartsWith("data:image/png;base64,", Assert.Single(references.Cast<Match>()).Groups[1].Value);
    }

    [Theory]
    [InlineData("cropped.png", false)]
    [InlineData("replacement.png", false)]
    [InlineData("redacted.png", true)]
    [InlineData("annotated.png", true)]
    public async Task SingleHtml_PreservesFinalScreenshotWithoutReencoding(string filename, bool useEditedPath)
    {
        var original = await WriteImageAsync("original.png", OriginalPng);
        var final = await WriteImageAsync(filename, EditedPng);
        var session = new RecordingSession { Title = "Edited screenshot" };
        session.Steps.Add(new RecordedStep
        {
            ScreenshotPath = useEditedPath ? original : final,
            EditedScreenshotPath = useEditedPath ? final : null
        });

        var result = await new MarkdownExporter().ExportAsync(session, _temp, GuideExportFormat.SingleHtml);
        var html = await File.ReadAllTextAsync(result.SingleHtmlPath);

        Assert.Equal(await File.ReadAllBytesAsync(final), Assert.Single(ReadEmbeddedImages(html)));
        Assert.DoesNotContain(OriginalPng, html);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task SingleHtml_PreservesStepOrderAndEscapesTitlesAndDescriptions()
    {
        var firstImage = await WriteImageAsync("first.png", OriginalPng);
        var secondImage = await WriteImageAsync("second.png", EditedPng);
        var session = new RecordingSession { Title = "Guide & <b>title</b>" };
        session.Steps.Add(new RecordedStep { Index = 1, UserTitle = "Second", ScreenshotPath = secondImage });
        session.Steps.Add(new RecordedStep
        {
            Index = 2,
            GeneratedTitle = "Generated",
            UserTitle = "First <img src=x onerror=alert(1)>",
            UserDescription = "</p><script>alert(1)</script> & \"quoted\"\nNext line",
            ScreenshotPath = firstImage
        });
        session.Steps.Reverse();

        var result = await new MarkdownExporter().ExportAsync(session, _temp, GuideExportFormat.SingleHtml);
        var html = await File.ReadAllTextAsync(result.SingleHtmlPath);
        var embeddedImages = ReadEmbeddedImages(html);

        Assert.Contains("<title>Guide &amp; &lt;b&gt;title&lt;/b&gt;</title>", html);
        Assert.Contains("<h1>Guide &amp; &lt;b&gt;title&lt;/b&gt;</h1>", html);
        Assert.Contains("<h2>Step 1: First &lt;img src=x onerror=alert(1)&gt;</h2>", html);
        Assert.Contains("<p>&lt;/p&gt;&lt;script&gt;alert(1)&lt;/script&gt; &amp; &quot;quoted&quot;", html);
        var description = Regex.Match(html, "<p>(&lt;/p&gt;.*?)</p>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Equal(session.Steps[0].UserDescription, System.Net.WebUtility.HtmlDecode(description));
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.DoesNotContain("Generated", html);
        Assert.True(html.IndexOf("<h2>Step 1:", StringComparison.Ordinal) < html.IndexOf("<h2>Step 2: Second", StringComparison.Ordinal));
        Assert.Equal(2, embeddedImages.Count);
        Assert.Equal(await File.ReadAllBytesAsync(firstImage), embeddedImages[0]);
        Assert.Equal(await File.ReadAllBytesAsync(secondImage), embeddedImages[1]);
    }

    [Fact]
    public async Task SingleHtml_HandlesManualAndMissingScreenshotsWithExistingWarning()
    {
        var session = new RecordingSession { Title = "Missing images" };
        session.Steps.Add(new RecordedStep { ActionType = StepActionType.Manual, UserTitle = "Manual", UserDescription = "Instructions" });
        session.Steps.Add(new RecordedStep { UserTitle = "Missing", ScreenshotPath = Path.Combine(_temp, "missing.png") });

        var result = await new MarkdownExporter().ExportAsync(session, _temp, GuideExportFormat.SingleHtml);
        var html = await File.ReadAllTextAsync(result.SingleHtmlPath);

        Assert.Equal("Step 2 screenshot was missing and was skipped.", Assert.Single(result.Warnings));
        Assert.Contains("<h2>Step 1: Manual</h2>", html);
        Assert.Contains("<p>Instructions</p>", html);
        Assert.Contains("<h2>Step 2: Missing</h2>", html);
        Assert.DoesNotContain("<img", html);
        Assert.Equal(result.SingleHtmlPath, Assert.Single(Directory.GetFileSystemEntries(result.ExportFolder)));
    }

    [Fact]
    public async Task SingleHtml_FallsBackToOriginalWhenEditedScreenshotIsMissing()
    {
        var image = await WriteImageAsync("original.png", OriginalPng);
        var session = new RecordingSession { Title = "Missing edit" };
        session.Steps.Add(new RecordedStep { ScreenshotPath = image, EditedScreenshotPath = Path.Combine(_temp, "missing.png") });

        var result = await new MarkdownExporter().ExportAsync(session, _temp, GuideExportFormat.SingleHtml);

        Assert.Equal(await File.ReadAllBytesAsync(image), Assert.Single(ReadEmbeddedImages(await File.ReadAllTextAsync(result.SingleHtmlPath))));
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData(GuideExportFormat.Markdown)]
    [InlineData(GuideExportFormat.Html)]
    [InlineData(GuideExportFormat.Markdown | GuideExportFormat.Html)]
    [InlineData(GuideExportFormat.Markdown | GuideExportFormat.SingleHtml)]
    [InlineData(GuideExportFormat.Html | GuideExportFormat.SingleHtml)]
    [InlineData(GuideExportFormat.Markdown | GuideExportFormat.Html | GuideExportFormat.SingleHtml)]
    public async Task ExportFormats_PreserveExistingOutputsAndCanIncludeSingleHtml(GuideExportFormat formats)
    {
        var original = await WriteImageAsync("original.png", OriginalPng);
        var edited = await WriteImageAsync("edited.png", EditedPng);
        var session = new RecordingSession { Title = "Formats" };
        session.Steps.Add(new RecordedStep { UserTitle = "Edited step", ScreenshotPath = original, EditedScreenshotPath = edited });

        var result = await new MarkdownExporter().ExportAsync(session, _temp, formats);
        var outputCount = 0;
        Assert.Equal(await File.ReadAllBytesAsync(edited), await File.ReadAllBytesAsync(Path.Combine(result.ExportFolder, "images", "step-001.png")));
        if (formats.HasFlag(GuideExportFormat.Markdown))
        {
            outputCount++;
            var markdown = await File.ReadAllTextAsync(result.MarkdownPath);
            Assert.Contains("## Step 1: Edited step", markdown);
            Assert.Contains("![Step 1](images/step-001.png)", markdown);
            Assert.DoesNotContain("data:image", markdown);
        }

        if (formats.HasFlag(GuideExportFormat.Html))
        {
            outputCount++;
            var html = await File.ReadAllTextAsync(result.HtmlPath);
            Assert.Contains("<h2>Step 1: Edited step</h2>", html);
            Assert.Contains("src=\"images/step-001.png\"", html);
            Assert.DoesNotContain("data:image", html);
        }

        if (formats.HasFlag(GuideExportFormat.SingleHtml))
        {
            outputCount++;
            Assert.Equal(await File.ReadAllBytesAsync(edited), Assert.Single(ReadEmbeddedImages(await File.ReadAllTextAsync(result.SingleHtmlPath))));
        }
        else
        {
            Assert.Empty(result.SingleHtmlPath);
            Assert.False(File.Exists(Path.Combine(result.ExportFolder, "guide-single.html")));
        }

        Assert.Equal(outputCount, result.OutputPaths.Count);
        Assert.Equal(outputCount, Directory.GetFiles(result.ExportFolder).Length);
        Assert.All(result.OutputPaths, path => Assert.True(File.Exists(path)));
    }

    private async Task<string> WriteImageAsync(string filename, string base64)
    {
        var source = Path.Combine(_temp, "source");
        Directory.CreateDirectory(source);
        var path = Path.Combine(source, filename);
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(base64));
        return path;
    }

    private static List<byte[]> ReadEmbeddedImages(string html)
    {
        return Regex.Matches(html, "<img src=\"data:image/png;base64,([^\"]+)\"")
            .Cast<Match>()
            .Select(match => Convert.FromBase64String(match.Groups[1].Value))
            .ToList();
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }
}
