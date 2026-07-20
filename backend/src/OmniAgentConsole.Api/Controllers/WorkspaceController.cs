using Microsoft.AspNetCore.Mvc;
using OmniAgentConsole.Application.Runtime;
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

    [HttpGet("files")]
    public IActionResult GetFiles()
    {
        if (!Directory.Exists(WorkspaceRoot))
        {
            return Ok(new List<WorkspaceNode>());
        }

        var nodes = BuildTree(WorkspaceRoot, "");
        return Ok(nodes);
    }

    [HttpGet("file")]
    public async Task<IActionResult> GetFileContent([FromQuery] string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BadRequest("Path is required.");
        }

        if (!WorkspacePathGuard.TryResolve(WorkspaceRoot, path, out var fullPath))
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

    [HttpDelete]
    public IActionResult DeleteNode([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BadRequest("Path is required.");
        }

        if (!WorkspacePathGuard.TryResolve(WorkspaceRoot, path, out var fullPath))
        {
            return BadRequest("Invalid path.");
        }

        if (string.Equals(fullPath, WorkspacePathGuard.NormalizeRoot(WorkspaceRoot), System.StringComparison.Ordinal))
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

    private List<WorkspaceNode> BuildTree(string dir, string relativePrefix)
    {
        var list = new List<WorkspaceNode>();
        try
        {
            var directories = Directory.GetDirectories(dir);
            foreach (var d in directories)
            {
                var name = Path.GetFileName(d);
                var relPath = string.IsNullOrEmpty(relativePrefix) ? name : $"{relativePrefix}/{name}";
                var children = BuildTree(d, relPath);
                list.Add(new WorkspaceNode(name, relPath, true, children));
            }

            var files = Directory.GetFiles(dir);
            foreach (var f in files)
            {
                var name = Path.GetFileName(f);
                var relPath = string.IsNullOrEmpty(relativePrefix) ? name : $"{relativePrefix}/{name}";
                list.Add(new WorkspaceNode(name, relPath, false));
            }
        }
        catch { }

        return list.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name).ToList();
    }
}

public sealed record WorkspaceNode(string Name, string Path, bool IsDirectory, List<WorkspaceNode>? Children = null);
