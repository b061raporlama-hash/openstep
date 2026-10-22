using System.Text.RegularExpressions;
using System.Text.Json;
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

        var result = await ExportDirectAsync(session);
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
        Assert.DoesNotMatch("(?i)<(?:link|iframe|object|embed)\\b|<script[^>]+src=|@import|url\\(", portableHtml);
        Assert.Contains("connect-src 'none'", portableHtml);
        Assert.DoesNotContain("adguard", portableHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("(?:src|href)=[\"'][^\"']+[\"']", portableHtml);
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

        var result = await ExportDirectAsync(session);
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
        var session = new RecordingSession { Title = "Türkçe {{STEP_DATA}} & <b>başlık</b>" };
        session.Steps.Add(new RecordedStep { Index = 1, UserTitle = "Second", ScreenshotPath = secondImage });
        session.Steps.Add(new RecordedStep
        {
            Index = 2,
            GeneratedTitle = "Generated",
            UserTitle = "First <img src=x onerror=alert(1)>",
            UserDescription = "</script><script>alert(1)</script> & \"quoted\"\nİkinci satır: ığüşöç",
            ScreenshotPath = firstImage
        });
        session.Steps.Reverse();

        var result = await ExportDirectAsync(session);
        var html = await File.ReadAllTextAsync(result.SingleHtmlPath);
        var embeddedImages = ReadEmbeddedImages(html);

        var title = Regex.Match(html, "<title>(.*?)</title>").Groups[1].Value;
        Assert.Equal(session.Title, System.Net.WebUtility.HtmlDecode(title));
        var steps = ReadSteps(html);
        Assert.Equal(1, steps[0].GetProperty("number").GetInt32());
        Assert.Equal(2, steps[1].GetProperty("number").GetInt32());
        Assert.Equal(session.Steps[0].DisplayTitle + "\n" + session.Steps[0].UserDescription, steps[0].GetProperty("description").GetString());
        Assert.Equal("Second", steps[1].GetProperty("description").GetString());
        Assert.Equal(2, Regex.Matches(html, "<script\\b").Count);
        Assert.DoesNotContain("</script><script>alert(1)", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.DoesNotContain("Generated", html);
        Assert.Contains("$('description').textContent", html);
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

        var result = await ExportDirectAsync(session);
        var html = await File.ReadAllTextAsync(result.SingleHtmlPath);

        Assert.Equal("Step 2 screenshot was missing and was skipped.", Assert.Single(result.Warnings));
        var steps = ReadSteps(html);
        Assert.Equal(2, steps.GetArrayLength());
        Assert.Equal("Manual\nInstructions", steps[0].GetProperty("description").GetString());
        Assert.Equal("Missing", steps[1].GetProperty("description").GetString());
        Assert.All(steps.EnumerateArray(), step => Assert.Equal(JsonValueKind.Null, step.GetProperty("src").ValueKind));
        Assert.Empty(ReadEmbeddedImages(html));
        Assert.Contains("Bu adımda ekran görüntüsü yok", html);
        Assert.Equal(result.SingleHtmlPath, Assert.Single(Directory.GetFileSystemEntries(result.ExportFolder)));
    }

    [Fact]
    public async Task SingleHtml_FallsBackToOriginalWhenEditedScreenshotIsMissing()
    {
        var image = await WriteImageAsync("original.png", OriginalPng);
        var session = new RecordingSession { Title = "Missing edit" };
        session.Steps.Add(new RecordedStep { ScreenshotPath = image, EditedScreenshotPath = Path.Combine(_temp, "missing.png") });

        var result = await ExportDirectAsync(session);

        Assert.Equal(await File.ReadAllBytesAsync(image), Assert.Single(ReadEmbeddedImages(await File.ReadAllTextAsync(result.SingleHtmlPath))));
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData(GuideExportFormat.SingleHtml)]
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
        if (formats.HasFlag(GuideExportFormat.Markdown) || formats.HasFlag(GuideExportFormat.Html))
        {
            Assert.Equal(await File.ReadAllBytesAsync(edited), await File.ReadAllBytesAsync(Path.Combine(result.ExportFolder, "images", "step-001.png")));
        }
        else
        {
            Assert.False(Directory.Exists(Path.Combine(result.ExportFolder, "images")));
        }
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

    [Fact]
    public async Task SingleHtml_EmptySessionHasAnEmptyStateAndNoImageFiles()
    {
        var result = await ExportDirectAsync(new RecordingSession());
        var html = await File.ReadAllTextAsync(result.SingleHtmlPath);

        Assert.Empty(ReadSteps(html).EnumerateArray());
        Assert.Contains("Bu kayıtta adım bulunmuyor.", html);
        Assert.Empty(result.Warnings);
        Assert.Equal(result.SingleHtmlPath, Assert.Single(Directory.GetFileSystemEntries(result.ExportFolder)));
    }

    [Fact]
    public async Task SingleHtml_CancelledExportDoesNotCreateAFile()
    {
        Directory.CreateDirectory(_temp);
        var output = Path.Combine(_temp, "cancelled.html");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SingleHtmlExporter().ExportAsync(new RecordingSession(), output, cancellation.Token));

        Assert.False(File.Exists(output));
    }

    private Task<ExportResult> ExportDirectAsync(RecordingSession session)
    {
        var outputDirectory = Path.Combine(_temp, "export");
        Directory.CreateDirectory(outputDirectory);
        return new SingleHtmlExporter().ExportAsync(session, Path.Combine(outputDirectory, "guide.html"));
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
        return ReadSteps(html).EnumerateArray()
            .Where(step => step.GetProperty("src").ValueKind == JsonValueKind.String)
            .Select(step => Convert.FromBase64String(step.GetProperty("src").GetString()!.Split(',', 2)[1]))
            .ToList();
    }

    private static JsonElement ReadSteps(string html)
    {
        var json = Regex.Match(html, "<script type=\"application/json\" id=\"step-data\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }
}
