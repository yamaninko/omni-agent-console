using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OmniAgentConsole.Application.Providers;

namespace OmniAgentConsole.Application.Runtime;

/// <summary>
/// Filesystem tools exposed to tool-calling agents. Every path is resolved
/// through <see cref="WorkspacePathGuard"/> against the task's workspace root,
/// so the model can never read or write outside it. One instance per task run
/// carries the written-file budget.
/// </summary>
public sealed class AgentWorkspaceTools
{
    public const int MaxFilesPerTask = 50;
    public const int MaxFileChars = 1_000_000;
    public const int MaxReadChars = 24_000;
    public const int MaxListEntries = 200;

    private readonly string workspaceRoot;
    private readonly List<string> writtenFiles = [];
    private int terminalRuns;

    public AgentWorkspaceTools(string workspaceRoot)
    {
        this.workspaceRoot = workspaceRoot;
    }

    public IReadOnlyList<string> WrittenFiles => writtenFiles;

    public const int MaxTerminalRunsPerTask = 6;

    public static IReadOnlyList<ToolDefinition> Definitions { get; } =
    [
        new ToolDefinition(
            "write_file",
            "Create or overwrite one file in the project workspace. Call this once per file with the complete file content.",
            """
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Relative file path inside the workspace, e.g. app/main.py" },
                "content": { "type": "string", "description": "Complete file content." }
              },
              "required": ["path", "content"]
            }
            """),
        new ToolDefinition(
            "read_file",
            "Read a file previously written to the project workspace.",
            """
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Relative file path inside the workspace." }
              },
              "required": ["path"]
            }
            """),
        new ToolDefinition(
            "list_files",
            "List the files currently present in the project workspace.",
            """
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Optional subdirectory to list; omit for the workspace root." }
              }
            }
            """),
        new ToolDefinition(
            "run_terminal",
            "Run a WHITELISTED test/lint command in the workspace (pytest, npm test, dotnet test, go test, ruff check, tsc --noEmit). No shell operators.",
            """
            {
              "type": "object",
              "properties": {
                "command": { "type": "string", "description": "Exact allow-listed command, e.g. pytest or npm test" }
              },
              "required": ["command"]
            }
            """)
    ];

    /// <summary>
    /// Executes one tool call. Failures are returned as messages (not thrown) so
    /// the model can see the error and correct itself on the next turn.
    /// </summary>
    public ToolExecutionResult Execute(string toolName, string argumentsJson)
    {
        JsonElement args;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return ToolExecutionResult.Failure($"Tool arguments for {toolName} were not valid JSON.");
        }

        return toolName switch
        {
            "write_file" => WriteFile(ReadStringArg(args, "path"), ReadStringArg(args, "content")),
            "read_file" => ReadFile(ReadStringArg(args, "path")),
            "list_files" => ListFiles(ReadStringArg(args, "path")),
            "run_terminal" => RunTerminal(ReadStringArg(args, "command")),
            _ => ToolExecutionResult.Failure(
                $"Unknown tool: {toolName}. Available tools: write_file, read_file, list_files, run_terminal.")
        };
    }

    private ToolExecutionResult RunTerminal(string? command)
    {
        var reject = TerminalSandboxPolicy.RejectReason(command);
        if (reject is not null)
        {
            return ToolExecutionResult.Failure(reject);
        }

        if (terminalRuns >= MaxTerminalRunsPerTask)
        {
            return ToolExecutionResult.Failure(
                $"run_terminal budget exhausted ({MaxTerminalRunsPerTask} runs/task). Summarize and finish.");
        }

        terminalRuns++;
        var cmd = command!.Trim();

        try
        {
            // Invoke via /bin/sh -c only after whitelist validation (no metacharacters allowed).
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                ArgumentList = { "-c", cmd },
                WorkingDirectory = workspaceRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // Avoid inheriting host secrets into model-visible output where possible.
            psi.Environment["PYTHONUNBUFFERED"] = "1";

            using var process = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null && stdout.Length < TerminalSandboxPolicy.MaxOutputChars)
                {
                    stdout.AppendLine(e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null && stderr.Length < TerminalSandboxPolicy.MaxOutputChars)
                {
                    stderr.AppendLine(e.Data);
                }
            };

            if (!process.Start())
            {
                return ToolExecutionResult.Failure("Failed to start process.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            var finished = process.WaitForExit(TerminalSandboxPolicy.DefaultTimeoutSeconds * 1000);
            if (!finished)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return ToolExecutionResult.Failure(
                    $"Command timed out after {TerminalSandboxPolicy.DefaultTimeoutSeconds}s: {cmd}");
            }

            process.WaitForExit();
            var combined = $"exit={process.ExitCode}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}";
            if (combined.Length > TerminalSandboxPolicy.MaxOutputChars)
            {
                combined = combined[..TerminalSandboxPolicy.MaxOutputChars] + "\n[truncated]";
            }

            return ToolExecutionResult.Ok(combined, null);
        }
        catch (Exception ex)
        {
            return ToolExecutionResult.Failure($"run_terminal failed: {ex.Message}");
        }
    }

    private ToolExecutionResult WriteFile(string? path, string? content)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolExecutionResult.Failure("write_file requires a non-empty 'path' argument.");
        }

        if (content is null)
        {
            return ToolExecutionResult.Failure("write_file requires a 'content' argument.");
        }

        if (content.Length > MaxFileChars)
        {
            return ToolExecutionResult.Failure($"File content exceeds the {MaxFileChars} character limit; split it into smaller files.");
        }

        if (writtenFiles.Count >= MaxFilesPerTask)
        {
            return ToolExecutionResult.Failure($"The {MaxFilesPerTask}-file budget for this task is exhausted; finish with a summary.");
        }

        if (!WorkspacePathGuard.TryResolve(workspaceRoot, path, out var fullPath))
        {
            return ToolExecutionResult.Failure($"Path '{path}' is outside the workspace and was rejected.");
        }

        try
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, content);
        }
        catch (Exception exception)
        {
            return ToolExecutionResult.Failure($"Could not write '{path}': {exception.Message}");
        }

        var relative = NormalizeRelative(path);
        if (!writtenFiles.Contains(relative, StringComparer.OrdinalIgnoreCase))
        {
            writtenFiles.Add(relative);
        }

        return ToolExecutionResult.Ok($"Wrote {relative} ({content.Length} chars).", relative);
    }

    private ToolExecutionResult ReadFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolExecutionResult.Failure("read_file requires a non-empty 'path' argument.");
        }

        if (!WorkspacePathGuard.TryResolve(workspaceRoot, path, out var fullPath))
        {
            return ToolExecutionResult.Failure($"Path '{path}' is outside the workspace and was rejected.");
        }

        if (!File.Exists(fullPath))
        {
            return ToolExecutionResult.Failure($"File '{path}' does not exist.");
        }

        try
        {
            var content = File.ReadAllText(fullPath);
            if (content.Length > MaxReadChars)
            {
                content = string.Concat(content.AsSpan(0, MaxReadChars), "\n[truncated]");
            }

            return ToolExecutionResult.Ok(content, NormalizeRelative(path));
        }
        catch (Exception exception)
        {
            return ToolExecutionResult.Failure($"Could not read '{path}': {exception.Message}");
        }
    }

    private ToolExecutionResult ListFiles(string? path)
    {
        var target = string.IsNullOrWhiteSpace(path) ? "." : path;
        if (!WorkspacePathGuard.TryResolve(workspaceRoot, target, out var fullPath))
        {
            return ToolExecutionResult.Failure($"Path '{path}' is outside the workspace and was rejected.");
        }

        if (!Directory.Exists(fullPath))
        {
            return ToolExecutionResult.Ok("(empty)", null);
        }

        try
        {
            var entries = Directory
                .EnumerateFiles(fullPath, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(fullPath, file).Replace('\\', '/'))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .Take(MaxListEntries + 1)
                .ToList();

            if (entries.Count == 0)
            {
                return ToolExecutionResult.Ok("(empty)", null);
            }

            var truncated = entries.Count > MaxListEntries;
            if (truncated)
            {
                entries.RemoveAt(entries.Count - 1);
            }

            var listing = string.Join('\n', entries) + (truncated ? "\n[truncated]" : string.Empty);
            return ToolExecutionResult.Ok(listing, null);
        }
        catch (Exception exception)
        {
            return ToolExecutionResult.Failure($"Could not list '{target}': {exception.Message}");
        }
    }

    private static string NormalizeRelative(string path)
    {
        var normalized = path.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/');
    }

    private static string? ReadStringArg(JsonElement args, string name)
    {
        return args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}

/// <summary>Outcome of one tool execution, fed back to the model as a tool message.</summary>
public sealed record ToolExecutionResult(bool Success, string Output, string? AffectedPath)
{
    public static ToolExecutionResult Ok(string output, string? affectedPath) => new(true, output, affectedPath);
    public static ToolExecutionResult Failure(string message) => new(false, message, null);
}
