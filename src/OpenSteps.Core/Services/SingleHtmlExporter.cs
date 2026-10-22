using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

public sealed class SingleHtmlExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ExportResult> ExportAsync(RecordingSession session, string outputFilePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var steps = new List<ExportStep>();
        var warnings = new List<string>();
        for (var i = 0; i < session.Steps.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = session.Steps[i];
            var number = i + 1;
            var title = string.IsNullOrWhiteSpace(step.DisplayTitle) ? $"Adım {number}" : step.DisplayTitle;
            var description = string.IsNullOrWhiteSpace(step.UserDescription)
                ? title
                : $"{title}\n{step.UserDescription}";
            string? imageSource = null;
            var screenshotPath = step.EffectiveScreenshotPath;
            if (!string.IsNullOrWhiteSpace(screenshotPath))
            {
                if (File.Exists(screenshotPath))
                {
                    var bytes = await File.ReadAllBytesAsync(screenshotPath, cancellationToken);
                    imageSource = $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
                }
                else
                {
                    warnings.Add($"Step {number} screenshot was missing and was skipped.");
                }
            }

            steps.Add(new ExportStep(number, description, imageSource));
        }

        using var stream = typeof(SingleHtmlExporter).Assembly.GetManifestResourceStream("OpenSteps.SingleHtmlViewer.html")!;
        using var reader = new StreamReader(stream);
        var template = await reader.ReadToEndAsync(cancellationToken);
        var sessionTitle = string.IsNullOrWhiteSpace(session.Title) ? "OpenSteps Guide" : session.Title.Trim();
        var json = JsonSerializer.Serialize(steps, JsonOptions);
        // Replace in one pass so placeholder-like user text remains literal.
        var html = Regex.Replace(template, @"\{\{(TITLE|STEP_COUNT|STEP_DATA)\}\}", match => match.Groups[1].Value switch
        {
            "TITLE" => HtmlEncoder.Default.Encode(sessionTitle),
            "STEP_COUNT" => steps.Count.ToString(CultureInfo.InvariantCulture),
            _ => json
        });
        var fullPath = Path.GetFullPath(outputFilePath);
        await File.WriteAllTextAsync(fullPath, html, cancellationToken);

        return new ExportResult
        {
            ExportFolder = Path.GetDirectoryName(fullPath)!,
            SingleHtmlPath = fullPath,
            OutputPaths = [fullPath],
            Warnings = warnings
        };
    }

    private sealed record ExportStep(int Number, string Description, string? Src);
}
