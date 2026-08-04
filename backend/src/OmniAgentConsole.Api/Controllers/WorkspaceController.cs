using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OmniAgentConsole.Api.Middleware;
using OmniAgentConsole.Application.Configuration;
using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Application.Workspace;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace OmniAgentConsole.Api.Controllers;

[ApiController]
[Route("api/workspace")]
public class WorkspaceController : ControllerBase
{
    private const string WorkspaceRoot = "/workspace";

    private readonly SharedLabOptions sharedLab;
    private readonly IWorkspaceProjectRunner projectRunner;

    public WorkspaceController(
        IOptions<SharedLabOptions> sharedLab,
        IWorkspaceProjectRunner projectRunner)
    {
        this.sharedLab = sharedLab.Value;
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
