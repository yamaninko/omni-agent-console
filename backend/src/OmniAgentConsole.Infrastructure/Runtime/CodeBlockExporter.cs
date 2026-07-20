using OmniAgentConsole.Application.Runtime;

namespace OmniAgentConsole.Infrastructure.Runtime;

/// <summary>
/// Legacy code export: splits a model's markdown answer into real workspace
/// files. Primary output today comes from the Coder tool loop; this exporter
/// remains the fallback for models without function-calling support. Handles
/// fenced blocks (first-line filepath comments, preceding-text filenames,
/// output/ fallback naming) and fence-less "// filepath:" annotated streams.
/// </summary>
public static class CodeBlockExporter
{
    public const int MaxExportFiles = 50;
    public const int MaxExportFileChars = 1_000_000;

    // Matches "// filepath: src/app.ts" style annotations (also #, <!--, /*) at line start.
    private static readonly System.Text.RegularExpressions.Regex FilepathMarkerRegex = new(
        @"(?m)^[ \t]*(?://|#|<!--|/\*)\s*(?:file:|filename:|filepath:)\s*([a-zA-Z0-9_\-\./\\]+\.[a-zA-Z0-9_]+)[^\r\n]*",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly HashSet<string> ValidExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "js", "ts", "json", "go", "cs", "py", "html", "css", "yml", "yaml", "sh", "bash", "md",
        "dockerfile", "txt", "sql", "conf", "ini", "rs", "c", "cpp", "h", "hpp", "java", "kt", "rb", "php"
    };

    // workspacePath must already be validated by WorkspacePathGuard; filenames from
    // model output are re-validated here because they can contain traversal attempts.
    public static (int Written, int Skipped) Export(string content, string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || string.IsNullOrWhiteSpace(content)) return (0, 0);

        try
        {
            if (!Directory.Exists(workspacePath))
            {
                Directory.CreateDirectory(workspacePath);
            }
        }
        catch { return (0, 0); }

        var matches = System.Text.RegularExpressions.Regex.Matches(
            content,
            @"(?s)```([a-zA-Z0-9_-]*)\r?\n(.*?)\r?\n```"
        );

        if (matches.Count == 0)
        {
            // Models frequently emit multi-file output WITHOUT markdown fences, as a
            // stream of "// filepath: ..." annotated sections. Split on those markers
            // so each section lands in its own file instead of one concatenated blob.
            var markers = FilepathMarkerRegex.Matches(content);
            if (markers.Count >= 2)
            {
                var sectionWritten = 0;
                var sectionSkipped = 0;
                for (var i = 0; i < markers.Count; i++)
                {
                    var start = markers[i].Index + markers[i].Length;
                    var end = i + 1 < markers.Count ? markers[i + 1].Index : content.Length;
                    var body = content[start..end].Trim('\r', '\n');
                    var relativePath = markers[i].Groups[1].Value.Trim();

                    if (sectionWritten >= MaxExportFiles || body.Length == 0 || body.Length > MaxExportFileChars
                        || !WorkspacePathGuard.TryResolve(workspacePath, relativePath, out var sectionPath))
                    {
                        sectionSkipped++;
                        continue;
                    }

                    try
                    {
                        var sectionDir = Path.GetDirectoryName(sectionPath);
                        if (!string.IsNullOrEmpty(sectionDir) && !Directory.Exists(sectionDir))
                        {
                            Directory.CreateDirectory(sectionDir);
                        }

                        File.WriteAllText(sectionPath, body + "\n");
                        sectionWritten++;
                    }
                    catch
                    {
                        sectionSkipped++;
                    }
                }

                return (sectionWritten, sectionSkipped);
            }

            string filename = "README.md";

            var filepathMatch = markers.Count == 1 ? markers[0] : null;
            if (filepathMatch is not null)
            {
                var extracted = Path.GetFileName(filepathMatch.Groups[1].Value.Trim());
                if (IsValidFilename(extracted))
                {
                    filename = extracted;
                }
            }
            else if (content.Contains("def ") || (content.Contains("import ") && !content.Contains("package ") && !content.Contains("func ")))
            {
                filename = "main.py";
            }
            else if (content.Contains("package ") || content.Contains("import ") || content.Contains("func "))
            {
                filename = "main.go";
            }
            else if (content.Contains("class ") || content.Contains("using System;"))
            {
                filename = "Program.cs";
            }
            else if (content.Contains("import express") || content.Contains("require("))
            {
                filename = "index.js";
            }

            if (content.Length > MaxExportFileChars || !WorkspacePathGuard.TryResolve(workspacePath, filename, out var singleFilePath))
            {
                return (0, 1);
            }

            try
            {
                File.WriteAllText(singleFilePath, content);
                return (1, 0);
            }
            catch
            {
                return (0, 1);
            }
        }

        int fileIndex = 1;
        int written = 0;
        int skippedCount = 0;
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var langTag = match.Groups[1].Value.Trim().ToLowerInvariant();
            var blockContent = match.Groups[2].Value.Trim();
            if (string.IsNullOrWhiteSpace(blockContent)) continue;

            if (written >= MaxExportFiles || blockContent.Length > MaxExportFileChars)
            {
                skippedCount++;
                continue;
            }

            string? filename = null;
            var lines = blockContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0)
            {
                var firstLine = lines[0].Trim();
                var cleanLine = firstLine
                    .Replace("//", "")
                    .Replace("/*", "")
                    .Replace("*/", "")
                    .Replace("#", "")
                    .Replace("<!--", "")
                    .Replace("-->", "")
                    .Replace("file:", "")
                    .Replace("filename:", "")
                    .Replace("filepath:", "")
                    .Trim();

                var ext = Path.GetExtension(cleanLine).TrimStart('.');
                if ((ValidExtensions.Contains(ext) || cleanLine.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)) && IsValidFilename(cleanLine))
                {
                    filename = cleanLine;
                    blockContent = string.Join(Environment.NewLine, lines.Skip(1));
                }
            }

            if (filename == null)
            {
                int blockIndex = match.Index;
                int startSearch = Math.Max(0, blockIndex - 150);
                var preceding = content.Substring(startSearch, blockIndex - startSearch);
                var fileMatches = System.Text.RegularExpressions.Regex.Matches(
                    preceding,
                    @"[a-zA-Z0-9_\-\./\\\\]+\.[a-zA-Z0-9_]+"
                );

                for (int i = fileMatches.Count - 1; i >= 0; i--)
                {
                    var possibleName = fileMatches[i].Value;
                    var ext = Path.GetExtension(possibleName).TrimStart('.');
                    if ((ValidExtensions.Contains(ext) || possibleName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)) && IsValidFilename(possibleName))
                    {
                        filename = possibleName;
                        break;
                    }
                }
            }

            if (filename == null)
            {
                string ext = "txt";
                if (!string.IsNullOrEmpty(langTag))
                {
                    ext = langTag switch
                    {
                        "go" => "go",
                        "python" or "py" => "py",
                        "javascript" or "js" => "js",
                        "typescript" or "ts" => "ts",
                        "csharp" or "cs" => "cs",
                        "html" => "html",
                        "css" => "css",
                        "json" => "json",
                        "bash" or "sh" => "sh",
                        "markdown" or "md" => "md",
                        "yaml" or "yml" => "yml",
                        "sql" => "sql",
                        _ => "txt"
                    };
                }
                // No filename could be inferred; keep these fallback files out of
                // the workspace root so they don't clutter the exported project.
                filename = $"output/output_{fileIndex}.{ext}";
                fileIndex++;
            }

            if (!WorkspacePathGuard.TryResolve(workspacePath, filename, out var fullPath))
            {
                skippedCount++;
                continue;
            }

            try
            {
                var fileDir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir))
                {
                    Directory.CreateDirectory(fileDir);
                }

                File.WriteAllText(fullPath, blockContent);
                written++;
            }
            catch
            {
                skippedCount++;
            }
        }

        return (written, skippedCount);
    }

    private static bool IsValidFilename(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains(" ") || text.Length > 100) return false;
        return text.Contains('.') && !text.Contains(':') && !text.Contains('?') && !text.Contains('&');
    }
}
