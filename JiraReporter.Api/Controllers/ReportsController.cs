using JiraReporter.Api.Data;
using JiraReporter.Api.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JiraReporter.Api.Controllers;

[ApiController]
[Route("api/reports")]
public sealed class ReportsController(AppDbContext db) : ControllerBase
{
    private readonly HashSet<string> ClosedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "done", "closed", "canceled", "cancelled", "resolved"
    };

    private static readonly HashSet<string> WipClosedStatuses = new(StringComparer.OrdinalIgnoreCase)
{
    "done", "closed", "resolved", "complete", "completed", "canceled", "cancelled"
};

    // ۱. اندپوینت دریافت لیست اسپرینت‌های موجود در پروژه
    [HttpGet("sprints")]
    public async Task<IActionResult> GetSprints([FromQuery] string? projectKey, CancellationToken ct)
    {
        var query = db.Issues.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(projectKey))
            query = query.Where(i => i.ProjectKey == projectKey);

        var sprints = await query
            .Where(i => !string.IsNullOrEmpty(i.SprintName))
            .Select(i => i.SprintName!)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync(ct);

        return Ok(sprints);
    }

    // ۲. اعمال فیلتر sprintName روی Cycle Time
    [HttpGet("cycle-time")]
    public async Task<IActionResult> CycleTime(
        [FromQuery] string? projectKey,
        [FromQuery] string? sprintName,
        CancellationToken ct)
    {
        var query = db.Issues.AsNoTracking().Where(i => i.Resolved != null);

        if (!string.IsNullOrWhiteSpace(projectKey))
            query = query.Where(i => i.ProjectKey == projectKey);

        if (!string.IsNullOrWhiteSpace(sprintName))
            query = query.Where(i => i.SprintName == sprintName);

        var doneIssues = await query
            .Select(i => new { i.Key, i.Created, Resolved = i.Resolved!.Value })
            .OrderBy(i => i.Resolved)
            .ToListAsync(ct);

        var result = doneIssues.Select(i => new
        {
            i.Key,
            ResolvedDate = i.Resolved.ToString("yyyy-MM-dd"),
            CycleTimeDays = Math.Round((i.Resolved - i.Created).TotalDays, 1)
        }).ToList();

        return Ok(result);
    }

    // ۱. اصلاح متد WIP Aging
    [HttpGet("wip-aging")]
    public async Task<IActionResult> WipAging(
        [FromQuery] string? projectKey,
        [FromQuery] string? sprintName,
        CancellationToken ct)
    {
        var query = db.Issues.AsNoTracking()
            .Where(i => i.Resolved == null && !ClosedStatuses.Contains(i.Status));

        if (!string.IsNullOrWhiteSpace(projectKey))
        {
            query = query.Where(i => i.ProjectKey == projectKey);
        }

        if (!string.IsNullOrWhiteSpace(sprintName))
        {
            query = query.Where(i => i.SprintName == sprintName);
        }

        var issues = await query.ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var result = issues
            .Select(i => new
            {
                key = i.Key,
                summary = i.Summary,
                status = i.Status,
                assignee = string.IsNullOrWhiteSpace(i.Assignee) ? "نامشخص" : i.Assignee,
                ageDays = Math.Round((now - i.Created).TotalDays, 1)
            })
            .OrderByDescending(i => i.ageDays) // اولویت با تسک‌هایی که بیشترین معطلی را دارند
            .ToList();

        return Ok(result);
    }

    // ۲. متد اضافه شده برای Sprint Scope (حل خطای ۴۰۴ چارت Scope)
    [HttpGet("sprint-scope")]
    public async Task<IActionResult> SprintScope(
        [FromQuery] string? projectKey,
        [FromQuery] string? sprintName,
        CancellationToken ct)
    {
        var query = db.Issues.AsNoTracking().Where(i => !string.IsNullOrEmpty(i.SprintName));

        if (!string.IsNullOrWhiteSpace(projectKey))
        {
            query = query.Where(i => i.ProjectKey == projectKey);
        }

        if (!string.IsNullOrWhiteSpace(sprintName))
        {
            query = query.Where(i => i.SprintName == sprintName);
        }

        var issues = await query
            .Select(i => new { i.SprintName, i.Status, i.StoryPoints })
            .ToListAsync(ct);

        var result = issues
            .GroupBy(i => i.SprintName!)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var total = g.Count();
                var completed = g.Count(i => ClosedStatuses.Contains(i.Status));
                var remaining = total - completed;

                return new
                {
                    sprintName = g.Key,
                    initialCommitment = total,
                    completedScope = completed,
                    remainingScope = remaining,
                    totalItems = total
                };
            })
            .ToList();

        return Ok(result);
    }

    // ۴. گلوگاه‌ها / Time in Status
    [HttpGet("time-in-status")]
    public async Task<IActionResult> TimeInStatus(
        [FromQuery] string? projectKey,
        [FromQuery] string? sprintName,
        CancellationToken ct)
    {
        var query = db.Issues.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(projectKey))
            query = query.Where(i => i.ProjectKey == projectKey);

        if (!string.IsNullOrWhiteSpace(sprintName))
            query = query.Where(i => i.SprintName == sprintName);

        var issues = await query
            .Include(i => i.StatusChanges)
            .ToListAsync(ct);

        // محاسبه بر اساس تسک‌های اسپرینت انتخابی
        var statusDurations = issues
            .GroupBy(i => i.Status ?? "نامشخص")
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count(),
                TotalHours = g.Sum(x => Math.Max(1, Math.Round((DateTime.UtcNow - x.Created).TotalHours, 1)))
            })
            .ToList();

        return Ok(statusDurations);
    }

    // ۵. نسبت باگ‌ها (Bug Ratio)
    [HttpGet("bug-ratio")]
    public async Task<IActionResult> BugRatio(
        [FromQuery] string? projectKey,
        [FromQuery] string? sprintName,
        CancellationToken ct)
    {
        var query = db.Issues.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(projectKey))
            query = query.Where(i => i.ProjectKey == projectKey);

        if (!string.IsNullOrWhiteSpace(sprintName))
            query = query.Where(i => i.SprintName == sprintName);

        var total = await query.CountAsync(ct);
        var bugs = await query.CountAsync(i => i.Type == "Bug", ct);

        return Ok(new
        {
            Total = total,
            Bugs = bugs,
            Ratio = total > 0 ? Math.Round((double)bugs / total * 100, 1) : 0
        });
    }


    // ۶. توزیع فصلی باگ‌ها، فیچرها/تسک‌ها و موارد علامت‌خورده به‌عنوان unplanned
    [HttpGet("quarterly-distribution")]
    public async Task<IActionResult> QuarterlyDistribution(
        [FromQuery] string? projectKey,
        CancellationToken ct)
    {
        var query = db.Issues.AsNoTracking().Where(issue => issue.Resolved != null);
        query = FilterByProject(query, projectKey);

        var issues = await query
            .Select(issue => new { issue.Type, issue.Summary, Resolved = issue.Resolved!.Value })
            .ToListAsync(ct);

        var result = issues
            .GroupBy(issue => new
            {
                Year = issue.Resolved.Year,
                Quarter = (issue.Resolved.Month - 1) / 3 + 1
            })
            .OrderBy(group => group.Key.Year)
            .ThenBy(group => group.Key.Quarter)
            .Select(group => new
            {
                year = group.Key.Year,
                quarter = group.Key.Quarter,
                totalIssues = group.Count(),
                bugs = group.Count(issue => string.Equals(issue.Type, "Bug", StringComparison.OrdinalIgnoreCase)),
                featureTasks = group.Count(issue =>
                    string.Equals(issue.Type, "Feature", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(issue.Type, "Task", StringComparison.OrdinalIgnoreCase)),
                // مدل Issue خاصیت Labels ندارد؛ از marker در Summary و نوع Bug استفاده می‌کنیم.
                unplanned = group.Count(issue =>
                    string.Equals(issue.Type, "Bug", StringComparison.OrdinalIgnoreCase) ||
                    (issue.Summary?.Contains("[unplanned]", StringComparison.OrdinalIgnoreCase) ?? false))
            })
            .ToList();

        return Ok(result);
    }

    // ۷. سرعت اسپرینت و نسبت انجام به تعهد (بر مبنای Story Points)
    [HttpGet("sprint-velocity")]
    public async Task<IActionResult> SprintVelocity(
        [FromQuery] string? projectKey,
        CancellationToken ct)
    {
        var query = db.Issues.AsNoTracking().Where(issue => !string.IsNullOrEmpty(issue.SprintName));
        query = FilterByProject(query, projectKey);

        var issues = await query
            .Select(issue => new { issue.SprintName, issue.StoryPoints, issue.Status })
            .ToListAsync(ct);

        var result = issues
            .GroupBy(issue => issue.SprintName!)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var committed = group.Sum(issue => issue.StoryPoints ?? 1d);
                var completed = group
                    .Where(issue => ClosedStatuses.Contains(issue.Status))
                    .Sum(issue => issue.StoryPoints ?? 1d);

                return new
                {
                    sprintName = group.Key,
                    committed,
                    completed,
                    sayDoRatio = committed > 0 ? Math.Round(completed / committed * 100, 1) : 0,
                    issueCount = group.Count(),
                    completedIssueCount = group.Count(issue => ClosedStatuses.Contains(issue.Status))
                };
            })
            .ToList();

        return Ok(result);
    }

    // ۸. سنجش بازگشت کار از وضعیت‌های بازبینی/تست/اتمام به وضعیت‌های فعال
    [HttpGet("rework-rate")]
    public async Task<IActionResult> ReworkRate(
        [FromQuery] string? projectKey,
        CancellationToken ct)
    {
        var query = db.Issues.AsNoTracking().Where(issue => !string.IsNullOrEmpty(issue.SprintName));
        query = FilterByProject(query, projectKey);

        var issues = await query
            .Include(issue => issue.StatusChanges)
            .ToListAsync(ct);

        static bool IsReworkFrom(string? status) =>
            !string.IsNullOrWhiteSpace(status) &&
            (status.Contains("review", StringComparison.OrdinalIgnoreCase) ||
             status.Contains("test", StringComparison.OrdinalIgnoreCase) ||
             status.Contains("qa", StringComparison.OrdinalIgnoreCase) ||
             status.Contains("done", StringComparison.OrdinalIgnoreCase) ||
             status.Contains("closed", StringComparison.OrdinalIgnoreCase));

        static bool IsReworkTo(string? status) =>
            !string.IsNullOrWhiteSpace(status) &&
            (status.Contains("progress", StringComparison.OrdinalIgnoreCase) ||
             status.Contains("todo", StringComparison.OrdinalIgnoreCase) ||
             status.Contains("to do", StringComparison.OrdinalIgnoreCase) ||
             status.Contains("reopen", StringComparison.OrdinalIgnoreCase));

        var result = issues
            .GroupBy(issue => issue.SprintName!)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var reworkTransitions = group
                    .SelectMany(issue => issue.StatusChanges)
                    .Count(change => IsReworkFrom(change.FromStatus) && IsReworkTo(change.ToStatus));
                var reworkedIssues = group.Count(issue => issue.StatusChanges
                    .Any(change => IsReworkFrom(change.FromStatus) && IsReworkTo(change.ToStatus)));
                var totalIssues = group.Count();

                return new
                {
                    sprintName = group.Key,
                    totalIssues,
                    reworkedIssues,
                    reworkTransitions,
                    reworkRate = totalIssues > 0 ? Math.Round((double)reworkedIssues / totalIssues * 100, 1) : 0
                };
            })
            .ToList();

        return Ok(result);
    }

    [HttpGet("workload")]
    public async Task<IActionResult> Workload(string? projectKey, CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var query = FilterByProject(db.Issues.AsNoTracking(), projectKey);
        var issues = await query.ToListAsync(cancellationToken);

        var workload = issues
            .GroupBy(issue => issue.Assignee ?? "Unassigned")
            .Select(group => new
            {
                assignee = group.Key,
                openCount = group.Count(IsOpen),
                resolvedLast30Days = group.Count(issue => issue.Resolved >= since),
                storyPointsOpen = group.Where(IsOpen).Sum(issue => issue.StoryPoints ?? 0)
            });

        return Ok(workload);
    }

    [HttpGet("individual-performance")]
    public async Task<IActionResult> IndividualPerformance(
        [FromQuery] string? projectKey,
        [FromQuery] string? sprintName,
        CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var query = FilterByProject(db.Issues.AsNoTracking(), projectKey);
        if (!string.IsNullOrWhiteSpace(sprintName))
            query = query.Where(issue => issue.SprintName == sprintName);

        var issues = await query
            .Include(issue => issue.StatusChanges)
            .ToListAsync(cancellationToken);

        static bool IsReworkTransition(IssueStatusChange change)
        {
            var from = change.FromStatus ?? string.Empty;
            var to = change.ToStatus ?? string.Empty;
            var fromReview = from.Contains("review", StringComparison.OrdinalIgnoreCase) ||
                from.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                from.Contains("qa", StringComparison.OrdinalIgnoreCase) ||
                from.Contains("done", StringComparison.OrdinalIgnoreCase) ||
                from.Contains("closed", StringComparison.OrdinalIgnoreCase);
            var toActive = to.Contains("progress", StringComparison.OrdinalIgnoreCase) ||
                to.Contains("todo", StringComparison.OrdinalIgnoreCase) ||
                to.Contains("to do", StringComparison.OrdinalIgnoreCase) ||
                to.Contains("reopen", StringComparison.OrdinalIgnoreCase);
            return fromReview && toActive;
        }

        var result = issues
            .GroupBy(issue => string.IsNullOrWhiteSpace(issue.Assignee) ? "Unassigned" : issue.Assignee!)
            .Select(group =>
            {
                var recentResolved = group.Where(issue => issue.Resolved >= since).ToList();
                var cycleTimes = recentResolved
                    .Select(issue => (issue.Resolved!.Value - issue.Created).TotalDays)
                    .Where(days => days >= 0)
                    .ToList();

                return new
                {
                    assignee = group.Key,
                    assignedCount = group.Count(),
                    openCount = group.Count(IsOpen),
                    resolvedLast30Days = recentResolved.Count,
                    storyPointsCompletedLast30Days = recentResolved.Sum(issue => issue.StoryPoints ?? 0),
                    averageCycleTimeDaysLast30Days = cycleTimes.Count > 0
                        ? Math.Round(cycleTimes.Average(), 1)
                        : 0,
                    reworkedIssues = group.Count(issue => issue.StatusChanges.Any(IsReworkTransition))
                };
            })
            .OrderByDescending(person => person.resolvedLast30Days)
            .ThenBy(person => person.assignee)
            .ToList();

        return Ok(result);
    }

    [HttpGet("worklog-users")]
    public async Task<IActionResult> WorklogUsers(CancellationToken ct)
    {
        var users = await db.JiraUsers.AsNoTracking().Where(user => db.Worklogs.Any(log => log.AuthorAccountId == user.AccountId))
            .OrderBy(user => user.DisplayName).Select(user => new { user.AccountId, user.DisplayName }).ToListAsync(ct);
        return Ok(users);
    }

    [HttpGet("worklogs")]
    public async Task<IActionResult> Worklogs([FromQuery] string? accountId, [FromQuery] string? projectKey, [FromQuery] string? sprintName, CancellationToken ct)
    {
        var query = from log in db.Worklogs.AsNoTracking()
                    join issue in db.Issues.AsNoTracking() on log.IssueKey equals issue.Key into issueGroup
                    from issue in issueGroup.DefaultIfEmpty()
                    join user in db.JiraUsers.AsNoTracking() on log.AuthorAccountId equals user.AccountId into userGroup
                    from user in userGroup.DefaultIfEmpty()
                    select new { log, issue, user };
        if (!string.IsNullOrWhiteSpace(accountId)) query = query.Where(row => row.log.AuthorAccountId == accountId);
        if (!string.IsNullOrWhiteSpace(projectKey)) query = query.Where(row => row.issue != null && row.issue.ProjectKey == projectKey);
        if (!string.IsNullOrWhiteSpace(sprintName)) query = query.Where(row => row.issue != null && row.issue.SprintName == sprintName);
        var entries = await query.OrderByDescending(row => row.log.Started).ToListAsync(ct);
        return Ok(new
        {
            totalHours = Math.Round(entries.Sum(row => row.log.TimeSpentSeconds) / 3600d, 2),
            worklogCount = entries.Count,
            daily = entries.GroupBy(row => new { Day = row.log.Started.ToString("yyyy-MM-dd"), Person = row.user == null ? "نامشخص" : row.user.DisplayName })
                .OrderBy(group => group.Key.Day).Select(group => new { day = group.Key.Day, person = group.Key.Person, hours = Math.Round(group.Sum(row => row.log.TimeSpentSeconds) / 3600d, 2) }),
            people = entries.GroupBy(row => new { Id = row.log.AuthorAccountId, Name = row.user == null ? "نامشخص" : row.user.DisplayName })
                .OrderBy(group => group.Key.Name).Select(group => new { accountId = group.Key.Id, person = group.Key.Name, hours = Math.Round(group.Sum(row => row.log.TimeSpentSeconds) / 3600d, 2), worklogCount = group.Count() }),
            issues = entries.GroupBy(row => new { row.log.IssueKey, Summary = row.issue == null ? "" : row.issue.Summary })
                .OrderByDescending(group => group.Sum(row => row.log.TimeSpentSeconds)).Take(20)
                .Select(group => new { issueKey = group.Key.IssueKey, summary = group.Key.Summary, hours = Math.Round(group.Sum(row => row.log.TimeSpentSeconds) / 3600d, 2), worklogCount = group.Count() })
        });
    }

    private IQueryable<Issue> FilterByProject(IQueryable<Issue> query, string? projectKey)
    {
        return string.IsNullOrWhiteSpace(projectKey)
            ? query
            : query.Where(issue => issue.ProjectKey == projectKey);
    }

    private bool IsOpen(Issue issue) => !ClosedStatuses.Contains(issue.Status);

    private object BuildStatistics(List<double> values)
    {
        if (values.Count == 0)
        {
            return new
            {
                average = 0d,
                p50 = 0d,
                p85 = 0d
            };
        }

        values.Sort();
        return new
        {
            average = values.Average(),
            p50 = Percentile(values, 0.50),
            p85 = Percentile(values, 0.85)
        };
    }

    private double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        return sortedValues[index];
    }

    private void AddStatusDurations(
        Issue issue,
        IDictionary<string, double> totals,
        DateTime now)
    {
        var changes = issue.StatusChanges
            .OrderBy(change => change.ChangedAt)
            .ToList();
        var currentStatus = changes.FirstOrDefault()?.FromStatus ?? issue.Status;
        var lastChangedAt = issue.Created;

        foreach (var change in changes)
        {
            AddDuration(totals, currentStatus ?? "Unknown", change.ChangedAt - lastChangedAt);
            currentStatus = change.ToStatus;
            lastChangedAt = change.ChangedAt;
        }

        AddDuration(totals, currentStatus ?? "Unknown", now - lastChangedAt);
    }

    private void AddDuration(IDictionary<string, double> totals, string status, TimeSpan duration)
    {
        if (totals.TryGetValue(status, out double statusTime))
        {
            totals[status] = statusTime + duration.TotalHours;
        }
        else
        {
            totals[status] = duration.TotalHours;
        }
    }
}
