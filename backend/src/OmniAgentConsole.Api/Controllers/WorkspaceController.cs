using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OmniAgentConsole.Api.Middleware;
using OmniAgentConsole.Application.Configuration;
using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Application.Workspace;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OmniAgentConsole.Api.Controllers;

[ApiController]
[Route("api/workspace")]
public class WorkspaceController : ControllerBase
{
    private const string WorkspaceRoot = "/workspace";

    private readonly SharedLabOptions sharedLab;
    private readonly WorkspaceImportOptions workspaceImport;
    private readonly IWorkspaceProjectRunner projectRunner;

    public WorkspaceController(
        IOptions<SharedLabOptions> sharedLab,
        IOptions<WorkspaceImportOptions> workspaceImport,
        IWorkspaceProjectRunner projectRunner)
    {
        this.sharedLab = sharedLab.Value;
        this.workspaceImport = workspaceImport.Value;
        this.projectRunner = projectRunner;
    }

    // In the shared-lab profile every student session gets its own effective
    // root; paths cannot resolve outside it even with traversal attempts,
    // because WorkspacePathGuard anchors resolution to this root.
    private string EffectiveRoot =>
        sharedLab.Enabled
        && !SharedLabHttp.IsAdmin(HttpContext)
        && SharedLabHttp.GetSessionId(HttpContext) is { } sessionId
            ? SharedLabPolicy.SessionRoot(WorkspaceRoot, sessionId)
            : WorkspaceRoot;

    [HttpGet("files")]
    public IActionResult GetFiles()
    {
        var root = EffectiveRoot;
        if (!Directory.Exists(root))
        {
            return Ok(new List<WorkspaceNode>());
        }

        var nodes = BuildTree(root, "");
        return Ok(nodes);
    }

    [HttpGet("file")]
    public async Task<IActionResult> GetFileContent([FromQuery] string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BadRequest("Path is required.");
        }

        if (!WorkspacePathGuard.TryResolve(EffectiveRoot, path, out var fullPath))
        {
            return BadRequest("Invalid path.");
        }

        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound("File not found.");
        }

        var content = await System.IO.File.ReadAllTextAsync(fullPath, cancellationToken);
        return Ok(new { Content = content });
    }

    /// <summary>
    /// Detects a runnable project near <paramref name="path"/> and returns
    /// copy-paste docker commands + suggested host port (P1).
    /// </summary>
    [HttpGet("project")]
    public ActionResult<ProjectDetectResponse> DetectProject([FromQuery] string? path)
    {
        var sessionId = sharedLab.Enabled && !SharedLabHttp.IsAdmin(HttpContext)
            ? SharedLabHttp.GetSessionId(HttpContext)
            : null;
        return Ok(projectRunner.Detect(EffectiveRoot, path, sessionId));
    }

    [HttpPost("project/up")]
    public async Task<ActionResult<ProjectRunActionResponse>> UpProject(
        [FromQuery] string? path,
        CancellationToken cancellationToken)
    {
        var sessionId = sharedLab.Enabled && !SharedLabHttp.IsAdmin(HttpContext)
            ? SharedLabHttp.GetSessionId(HttpContext)
            : null;
        var result = await projectRunner.UpAsync(EffectiveRoot, path, sessionId, cancellationToken);
        return result.Ok ? Ok(result) : StatusCode(result.State == "disabled" ? 503 : 400, result);
    }

    [HttpPost("project/down")]
    public async Task<ActionResult<ProjectRunActionResponse>> DownProject(
        [FromQuery] string? path,
        CancellationToken cancellationToken)
    {
        var sessionId = sharedLab.Enabled && !SharedLabHttp.IsAdmin(HttpContext)
            ? SharedLabHttp.GetSessionId(HttpContext)
            : null;
        var result = await projectRunner.DownAsync(EffectiveRoot, path, sessionId, cancellationToken);
        return result.Ok ? Ok(result) : StatusCode(result.State == "disabled" ? 503 : 400, result);
    }

    [HttpGet("project/status")]
    public async Task<ActionResult<ProjectRunStatusResponse>> ProjectStatus(
        [FromQuery] string? path,
        CancellationToken cancellationToken)
    {
        var sessionId = sharedLab.Enabled && !SharedLabHttp.IsAdmin(HttpContext)
            ? SharedLabHttp.GetSessionId(HttpContext)
            : null;
        return Ok(await projectRunner.StatusAsync(EffectiveRoot, path, sessionId, cancellationToken));
    }

    /// <summary>
    /// SSRF-safe proxy for the in-console mini Postman: only localhost +
    /// the workspace runner port range for the selected project.
    /// </summary>
    [HttpPost("project/proxy")]
    public async Task<ActionResult<ProjectProxyResponse>> ProxyProject(
        [FromBody] ProjectProxyRequest request,
        CancellationToken cancellationToken)
    {
        var sessionId = sharedLab.Enabled && !SharedLabHttp.IsAdmin(HttpContext)
            ? SharedLabHttp.GetSessionId(HttpContext)
            : null;
        var result = await projectRunner.ProxyAsync(EffectiveRoot, request, sessionId, cancellationToken);
        return result.Ok ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Lists top-level folders available under the host import mount (copy sources).
    /// Empty when the mount is missing or disabled.
    /// </summary>
    [HttpGet("import-sources")]
    public IActionResult ListImportSources()
    {
        if (!workspaceImport.Enabled
            || string.IsNullOrWhiteSpace(workspaceImport.HostRoot)
            || !Directory.Exists(workspaceImport.HostRoot))
        {
            return Ok(new ImportSourcesResponse(Enabled: false, HostRoot: workspaceImport.HostRoot, Sources: []));
        }

        var sources = new List<ImportSourceDto>();
        try
        {
            foreach (var dir in Directory.GetDirectories(workspaceImport.HostRoot))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name) || name.StartsWith('.'))
                {
                    continue;
                }

                sources.Add(new ImportSourceDto(name, name));
            }
        }
        catch
        {
            // unreadable mount
        }

        return Ok(new ImportSourcesResponse(
            Enabled: true,
            HostRoot: workspaceImport.HostRoot,
            Sources: sources.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList()));
    }

    /// <summary>
    /// Copies a folder from the host import mount into /workspace/{projectName}.
    /// This is the reliable "add existing project" path for local Docker (server-side copy).
    /// </summary>
    [HttpPost("import-from-host")]
    public IActionResult ImportFromHost([FromBody] ImportFromHostRequest request)
    {
        if (!workspaceImport.Enabled
            || string.IsNullOrWhiteSpace(workspaceImport.HostRoot)
            || !Directory.Exists(workspaceImport.HostRoot))
        {
            return BadRequest(new
            {
                error = "Host import is not configured. Set HOST_IMPORT_DIR in .env (e.g. your projects folder) and restart compose, or use browser folder upload."
            });
        }

        if (!WorkspaceFolderPolicy.TryNormalizeProjectName(request?.Source, out var sourceName, out var sourceError))
        {
            return BadRequest(new { error = sourceError });
        }

        var projectNameRaw = string.IsNullOrWhiteSpace(request?.ProjectName) ? sourceName : request!.ProjectName;
        if (!WorkspaceFolderPolicy.TryNormalizeProjectName(projectNameRaw, out var projectName, out var nameError))
        {
            return BadRequest(new { error = nameError });
        }

        // Resolve source under host import root only (same guard pattern as workspace).
        if (!WorkspacePathGuard.TryResolve(workspaceImport.HostRoot, sourceName, out var sourceDir)
            || !Directory.Exists(sourceDir))
        {
            return NotFound(new { error = $"Source folder '{sourceName}' not found under host import root." });
        }

        var root = EffectiveRoot;
        if (!Directory.Exists(root))
        {
            Directory.CreateDirectory(root);
        }

        if (!WorkspacePathGuard.TryResolve(root, projectName, out var destDir))
        {
            return BadRequest(new { error = "Invalid project path." });
        }

        if (Directory.Exists(destDir) || System.IO.File.Exists(destDir))
        {
            return Conflict(new { error = $"Folder '{projectName}' already exists in workspace. Delete it first or pick another name.", path = projectName });
        }

        try
        {
            var (files, bytes, skippedDirs) = WorkspaceFolderPolicy.CopyDirectoryIntoWorkspace(
                root, projectName, sourceDir, destDir);

            return Ok(new ImportWorkspaceProjectResponse(
                Path: projectName,
                FilesWritten: files,
                FilesSkipped: skippedDirs,
                BytesWritten: bytes,
                SkipSamples: skippedDirs > 0
                    ? [$"skipped {skippedDirs} junk dir(s) (node_modules/.git/…)"]
                    : []));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Copy failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Creates an empty project folder under the effective workspace root
    /// (e.g. /workspace/my-api). Name is validated by <see cref="WorkspaceFolderPolicy"/>.
    /// </summary>
    [HttpPost("folders")]
    public IActionResult CreateFolder([FromBody] CreateWorkspaceFolderRequest request)
    {
        if (!WorkspaceFolderPolicy.TryNormalizeProjectName(request?.Name, out var name, out var error))
        {
            return BadRequest(new { error });
        }

        var root = EffectiveRoot;
        if (!Directory.Exists(root))
        {
            Directory.CreateDirectory(root);
        }

        if (!WorkspacePathGuard.TryResolve(root, name, out var fullPath))
        {
            return BadRequest(new { error = "Invalid project path." });
        }

        if (Directory.Exists(fullPath) || System.IO.File.Exists(fullPath))
        {
            return Conflict(new { error = $"Folder '{name}' already exists.", path = name });
        }

        try
        {
            Directory.CreateDirectory(fullPath);
            return Created($"/api/workspace/files?path={Uri.EscapeDataString(name)}", new CreateWorkspaceFolderResponse(name, name));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Failed to create folder: {ex.Message}" });
        }
    }

    /// <summary>
    /// Imports a local project tree from the browser (folder picker / webkitdirectory).
    /// Multipart form: projectName + files[] (FileName = relative path under project).
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(WorkspaceFolderPolicy.MaxTotalBytes + (2 * 1024 * 1024))]
    [RequestFormLimits(MultipartBodyLengthLimit = WorkspaceFolderPolicy.MaxTotalBytes + (2 * 1024 * 1024))]
    public async Task<IActionResult> ImportProject(
        [FromForm] string? projectName,
        [FromForm] List<IFormFile>? files,
        CancellationToken cancellationToken)
    {
        if (!WorkspaceFolderPolicy.TryNormalizeProjectName(projectName, out var name, out var error))
        {
            return BadRequest(new { error });
        }

        files ??= [];
        if (files.Count == 0)
        {
            return BadRequest(new { error = "No files provided. Select a folder with at least one file." });
        }

        if (files.Count > WorkspaceFolderPolicy.MaxFilesPerImport)
        {
            return BadRequest(new
            {
                error = $"Too many files (max {WorkspaceFolderPolicy.MaxFilesPerImport}). Exclude node_modules/.git or import a smaller tree."
            });
        }

        var root = EffectiveRoot;
        if (!Directory.Exists(root))
        {
            Directory.CreateDirectory(root);
        }

        if (!WorkspacePathGuard.TryResolve(root, name, out var projectRoot))
        {
            return BadRequest(new { error = "Invalid project path." });
        }

        if (Directory.Exists(projectRoot) || System.IO.File.Exists(projectRoot))
        {
            return Conflict(new { error = $"Folder '{name}' already exists. Delete it first or choose another name.", path = name });
        }

        long totalBytes = 0;
        var written = 0;
        var skipped = 0;
        var skipSamples = new List<string>();

        try
        {
            Directory.CreateDirectory(projectRoot);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Prefer ContentDisposition filename / FileName as relative path (FormData third arg).
                var rawRel = file.FileName;
                if (!WorkspaceFolderPolicy.TryNormalizeImportRelativePath(rawRel, name, out var rel, out var skipReason))
                {
                    skipped++;
                    if (skipSamples.Count < 8 && !string.IsNullOrEmpty(skipReason))
                    {
                        skipSamples.Add($"{rawRel}: {skipReason}");
                    }

                    continue;
                }

                if (WorkspaceFolderPolicy.ShouldRejectFileSize(file.Length, out var sizeError))
                {
                    // Clean up partial import
                    try { Directory.Delete(projectRoot, true); } catch { /* best effort */ }
                    return BadRequest(new { error = $"{sizeError} Offending: {rel}" });
                }

                totalBytes += file.Length;
                if (totalBytes > WorkspaceFolderPolicy.MaxTotalBytes)
                {
                    try { Directory.Delete(projectRoot, true); } catch { /* best effort */ }
                    return BadRequest(new
                    {
                        error = $"Import exceeds {WorkspaceFolderPolicy.MaxTotalBytes / (1024 * 1024)} MiB total. Exclude large binaries or node_modules."
                    });
                }

                var combined = $"{name}/{rel}";
                if (!WorkspacePathGuard.TryResolve(root, combined, out var fullFilePath))
                {
                    skipped++;
                    if (skipSamples.Count < 8)
                    {
                        skipSamples.Add($"{rel}: outside root");
                    }

                    continue;
                }

                var parent = Path.GetDirectoryName(fullFilePath);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                await using (var stream = System.IO.File.Create(fullFilePath))
                {
                    await file.CopyToAsync(stream, cancellationToken);
                }

                written++;
            }

            if (written == 0)
            {
                try { Directory.Delete(projectRoot, true); } catch { /* best effort */ }
                return BadRequest(new
                {
                    error = "No files were imported (all skipped or empty). Avoid selecting only node_modules/.git.",
                    skipped,
                    skipSamples
                });
            }

            return Ok(new ImportWorkspaceProjectResponse(
                Path: name,
                FilesWritten: written,
                FilesSkipped: skipped,
                BytesWritten: totalBytes,
                SkipSamples: skipSamples));
        }
        catch (OperationCanceledException)
        {
            try { if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, true); } catch { /* best effort */ }
            throw;
        }
        catch (Exception ex)
        {
            try { if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, true); } catch { /* best effort */ }
            return StatusCode(500, new { error = $"Import failed: {ex.Message}" });
        }
    }

    [HttpDelete]
    public IActionResult DeleteNode([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BadRequest("Path is required.");
        }

        var root = EffectiveRoot;
        if (!WorkspacePathGuard.TryResolve(root, path, out var fullPath))
        {
            return BadRequest("Invalid path.");
        }

        if (string.Equals(fullPath, WorkspacePathGuard.NormalizeRoot(root), System.StringComparison.Ordinal))
        {
            return BadRequest("Cannot delete the workspace root.");
        }

        if (Directory.Exists(fullPath))
        {
            try
            {
                Directory.Delete(fullPath, true);
                return NoContent();
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, $"Failed to delete directory: {ex.Message}");
            }
        }
        else if (System.IO.File.Exists(fullPath))
        {
            try
            {
                System.IO.File.Delete(fullPath);
                return NoContent();
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, $"Failed to delete file: {ex.Message}");
            }
        }

        return NotFound("File or directory not found.");
    }

    // Heavy dependency trees must never be walked recursively over a Docker
    // bind mount — on Windows (WSL2 virtiofs/9p) this alone can pin a core.
    private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules",
        ".git",
        ".svn",
        ".hg",
        "bin",
        "obj",
        "dist",
        "build",
        "out",
        "target",
        "vendor",
        "__pycache__",
        ".venv",
        "venv",
        ".tox",
        ".mypy_cache",
        ".pytest_cache",
        ".next",
        ".nuxt",
        ".angular",
        "coverage",
        ".idea",
        ".vs",
        ".vscode"
    };

    private const int MaxTreeDepth = 12;
    private const int MaxTreeNodes = 4000;

    private List<WorkspaceNode> BuildTree(string dir, string relativePrefix)
    {
        var remaining = MaxTreeNodes;
        return BuildTree(dir, relativePrefix, depth: 0, ref remaining);
    }

    private List<WorkspaceNode> BuildTree(string dir, string relativePrefix, int depth, ref int remaining)
    {
        var list = new List<WorkspaceNode>();
        if (remaining <= 0 || depth > MaxTreeDepth)
        {
            return list;
        }

        try
        {
            var directories = Directory.GetDirectories(dir);
            foreach (var d in directories)
            {
                if (remaining <= 0)
                {
                    break;
                }

                var name = Path.GetFileName(d);
                if (SkippedDirectoryNames.Contains(name))
                {
                    // Surface the folder so users know it exists, but do not
                    // recurse into multi-thousand-file dependency trees.
                    var skippedPath = string.IsNullOrEmpty(relativePrefix) ? name : $"{relativePrefix}/{name}";
                    list.Add(new WorkspaceNode(name, skippedPath, true, []));
                    remaining--;
                    continue;
                }

                var relPath = string.IsNullOrEmpty(relativePrefix) ? name : $"{relativePrefix}/{name}";
                remaining--;
                var children = BuildTree(d, relPath, depth + 1, ref remaining);
                list.Add(new WorkspaceNode(name, relPath, true, children));
            }

            var files = Directory.GetFiles(dir);
            foreach (var f in files)
            {
                if (remaining <= 0)
                {
                    break;
                }

                var name = Path.GetFileName(f);
                var relPath = string.IsNullOrEmpty(relativePrefix) ? name : $"{relativePrefix}/{name}";
                list.Add(new WorkspaceNode(name, relPath, false));
                remaining--;
            }
        }
        catch
        {
            // Ignore unreadable directories (permissions / race with agent writes).
        }

        return list.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name).ToList();
    }
}

public sealed record WorkspaceNode(string Name, string Path, bool IsDirectory, List<WorkspaceNode>? Children = null);

public sealed record CreateWorkspaceFolderRequest(string? Name);

public sealed record CreateWorkspaceFolderResponse(string Name, string Path);

public sealed record ImportWorkspaceProjectResponse(
    string Path,
    int FilesWritten,
    int FilesSkipped,
    long BytesWritten,
    IReadOnlyList<string> SkipSamples);

public sealed record ImportFromHostRequest(string? Source, string? ProjectName);

public sealed record ImportSourceDto(string Name, string Path);

public sealed record ImportSourcesResponse(bool Enabled, string HostRoot, IReadOnlyList<ImportSourceDto> Sources);
