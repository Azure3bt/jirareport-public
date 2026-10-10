using JiraReporter.Api.Data;
using JiraReporter.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JiraReporter.Api.Controllers;

[ApiController]
[Route("api/sync")]
public sealed class SyncController(JiraSyncService sync, AppDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Run(CancellationToken cancellationToken)
    {
        var issuesSynced = await sync.SyncAllAsync(cancellationToken);
        return Ok(new { issuesSynced });
    }

    [HttpGet("logs")]
    public async Task<IActionResult> Logs(CancellationToken cancellationToken)
    {
        var logs = await db.SyncLogs
            .AsNoTracking()
            .OrderByDescending(log => log.StartedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        return Ok(logs);
    }
}
