// JiraSeeder-Reports.cs
// Add this file to JiraReporter.Api (the project already references EF Core).
// Run once from application startup, a development-only endpoint, or a small scoped tool:
//   await JiraSeeder.SeedAsync(db, projectKey: "DEMO", issueCount: 120, cancellationToken);
//
// This seeds the local reporting database directly. Unlike Jira REST, it can create
// historical Created/Updated values and status histories, which are required for
// deterministic cycle-time and WIP-aging report samples. Do NOT run in production.

using JiraReporter.Api.Data;
using JiraReporter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace JiraReporter.Api.Data;

public static class JiraDataInitializer
{
    private const int MaximumIssues = 500;
    private static readonly double[] PointValues = [1, 2, 3, 5, 8, 13];
    private static readonly string[] Assignees =
        ["Alex Morgan", "Samira Ahmadi", "Jordan Lee", "Nima Karimi", "Taylor Kim", "Roya Hashemi"];
    private static readonly string[] Sprints =
        ["Sprint 01 - Foundation", "Sprint 02 - Payments", "Sprint 03 - Reliability", "Sprint 04 - Analytics"];
    private static readonly string[] Areas =
        ["Payment gateway", "Customer portal", "Reporting pipeline", "Identity service", "Mobile checkout",
         "Notification worker", "Ledger reconciliation", "Search indexing", "Admin dashboard", "Data export"];
    private static readonly string[] Actions =
        ["Add retry handling", "Improve validation", "Investigate intermittent failure", "Refactor request flow",
         "Add audit logging", "Reduce response latency", "Support bulk operations", "Improve error messaging",
         "Add monitoring metrics", "Update integration tests", "Fix edge-case calculation", "Document recovery steps"];

    /// <summary>
    /// Idempotently creates or refreshes a deterministic set of report-friendly sample issues.
    /// Existing non-seeder issues are not changed or deleted. Re-running refreshes only keys
    /// with the &lt;projectKey&gt;-SEED- prefix.
    /// </summary>
    public static async Task<int> SeedAsync(
        AppDbContext db,
        string projectKey = "JR",
        int issueCount = 120,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        projectKey = projectKey.Trim().ToUpperInvariant();
        if (issueCount < 1 || issueCount > MaximumIssues)
            throw new ArgumentOutOfRangeException(nameof(issueCount), $"Use a count between 1 and {MaximumIssues}.");

        var project = await db.Projects.FindAsync([projectKey], cancellationToken);
        if (project is null)
        {
            db.Projects.Add(new Project { IssueKey = projectKey, Name = $"{projectKey} reporting demo" });
        }

        var prefix = $"{projectKey}-SEED-";
        var existingIssues = await db.Issues
            .Include(issue => issue.StatusChanges)
            .Where(issue => issue.ProjectKey == projectKey && issue.Key.StartsWith(prefix))
            .ToListAsync(cancellationToken);
        var existingByKey = existingIssues.ToDictionary(issue => issue.Key, StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var changesToRemove = existingIssues.SelectMany(issue => issue.StatusChanges).ToList();
        db.StatusChanges.RemoveRange(changesToRemove);

        for (var i = 0; i < issueCount; i++)
        {
            var key = $"{prefix}{i + 1:0000}";
            if (!existingByKey.TryGetValue(key, out var issue))
            {
                issue = new Issue { Key = key };
                db.Issues.Add(issue);
            }
            else
            {
                issue.StatusChanges.Clear();
            }

            Populate(issue, projectKey, i, now);
        }

        // Shrinking issueCount removes only rows owned by this seeder.
        var keepThrough = issueCount;
        var obsolete = existingIssues.Where(issue =>
        {
            var suffix = issue.Key[prefix.Length..];
            return int.TryParse(suffix, out var number) && number > keepThrough;
        }).ToList();
        if (obsolete.Count > 0)
            db.Issues.RemoveRange(obsolete);

        await SeedWorklogsAsync(db, projectKey, issueCount, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return issueCount;
    }

    private static async Task SeedWorklogsAsync(AppDbContext db, string projectKey, int issueCount, DateTime now, CancellationToken cancellationToken)
    {
        var users = new[]
        {
            new JiraUser { AccountId = $"{projectKey}-DEMO-ALICE", DisplayName = "Alice Morgan", EmailAddress = "alice@example.test" },
            new JiraUser { AccountId = $"{projectKey}-DEMO-BOB", DisplayName = "Bob Smith", EmailAddress = "bob@example.test" },
            new JiraUser { AccountId = $"{projectKey}-DEMO-CAROL", DisplayName = "Carol Chen", EmailAddress = "carol@example.test" }
        };
        foreach (var seededUser in users)
        {
            var user = await db.JiraUsers.FindAsync([seededUser.AccountId], cancellationToken);
            if (user is null) db.JiraUsers.Add(seededUser);
            else { user.DisplayName = seededUser.DisplayName; user.EmailAddress = seededUser.EmailAddress; }
        }

        var logPrefix = $"{projectKey}-SEED-WL-";
        var existingLogs = await db.Worklogs.Where(log => log.WorklogId.StartsWith(logPrefix)).ToListAsync(cancellationToken);
        var existingById = existingLogs.ToDictionary(log => log.WorklogId, StringComparer.Ordinal);
        var logCount = Math.Min(48, issueCount * 2);
        for (var i = 0; i < logCount; i++)
        {
            var id = $"{logPrefix}{i + 1:0000}";
            var issueNumber = i % Math.Min(issueCount, 24) + 1;
            var log = existingById.TryGetValue(id, out var current) ? current : new Worklog { WorklogId = id };
            log.IssueKey = $"{projectKey}-SEED-{issueNumber:0000}";
            log.AuthorAccountId = users[i % users.Length].AccountId;
            log.Started = now.AddDays(-(i * 3 % 45)).AddHours(9 + i % 8);
            log.TimeSpentSeconds = (1 + i % 5) * 1800;
            log.Comment = new[] { "Implementation", "Code review", "Testing", "Documentation" }[i % 4];
            if (current is null) db.Worklogs.Add(log);
        }

        var obsoleteLogs = existingLogs.Where(log =>
            int.TryParse(log.WorklogId[logPrefix.Length..], out var number) && number > logCount).ToList();
        if (obsoleteLogs.Count > 0) db.Worklogs.RemoveRange(obsoleteLogs);
    }

    private static void Populate(Issue issue, string projectKey, int i, DateTime now)
    {
        var ageDays = i % 4 == 0 ? 7 + (i * 7 % 23) : 35 + (i * 13 % 145);
        var created = now.AddDays(-ageDays).AddHours(-((i * 5) % 24));
        var state = i % 12;
        var finalStatus = state switch
        {
            0 or 1 or 5 or 11 => "Done",
            2 or 6 => "In Progress",
            3 or 10 => "In Review",
            7 => "Closed",
            8 => "Canceled",
            _ => "To Do"
        };

        var remainder = i % 5;
        var type = remainder switch
        {
            0 => "Bug",
            4 => "Bug",
            1 => "Story",
            2 => "Story",
            _ => "Task"
        };

        var area = Areas[i % Areas.Length];
        var action = Actions[(i * 7 + i / 3) % Actions.Length];
        var assigned = i % 11 == 0 ? null : Assignees[(i * 3 + i / 5) % Assignees.Length];
        var sprint = i % 7 == 0 ? null : Sprints[(i / 3 + i % 4) % Sprints.Length];
        var points = type == "Bug" ? PointValues[(i + 1) % 4] : PointValues[(i * 5 + 2) % PointValues.Length];

        issue.Summary = $"{area}: {action} #{i + 1:000}";
        issue.Type = type;
        issue.Status = finalStatus;
        issue.Assignee = assigned;
        issue.Reporter = Assignees[(i * 2 + 1) % Assignees.Length];
        issue.ProjectKey = projectKey;
        issue.Created = created;
        issue.SprintName = sprint;
        issue.StoryPoints = points;
        issue.Resolved = null;

        var history = new List<(string From, string To, DateTime At)>();
        var inProgressAt = created.AddDays(1 + i % 3);
        var reviewAt = inProgressAt.AddDays(1 + i % 2);
        DateTime? resolvedAt = null;

        if (finalStatus is "Done" or "Closed" or "Canceled")
        {
            // All resolved samples have enough age for a meaningful, positive cycle time.
            var resolutionOffset = Math.Min(ageDays - 1, 4 + i % 8);
            resolvedAt = created.AddDays(Math.Max(2, resolutionOffset)).AddHours(i % 9);
            var doneStatus = finalStatus;
            history.Add(("To Do", "In Progress", inProgressAt));
            if (i % 3 == 1)
            {
                history.Add(("In Progress", "In Review", reviewAt));
                history.Add(("In Review", "In Progress", reviewAt.AddHours(12))); // review bounce-back
            }
            else
            {
                history.Add(("In Progress", "In Review", reviewAt));
            }
            history.Add((i % 3 == 1 ? "In Progress" : "In Review", doneStatus, resolvedAt.Value));
        }
        else if (finalStatus == "In Progress")
        {
            history.Add(("To Do", "In Progress", inProgressAt));
        }
        else if (finalStatus == "In Review")
        {
            history.Add(("To Do", "In Progress", inProgressAt));
            history.Add(("In Progress", "In Review", reviewAt));
        }

        // Many unresolved issues are intentionally stale (>7 days); the remainder are fresh.
        var isFresh = i % 5 == 2;
        var updated = history.Count == 0 ? created : history[^1].At;
        if (finalStatus is not ("Done" or "Closed" or "Canceled") && isFresh)
        {
            var recent = now.AddDays(-(1 + i % 4)).AddHours(-(i % 8));
            if (recent > created)
                updated = recent;
        }

        issue.Updated = resolvedAt ?? updated;
        issue.Resolved = resolvedAt;
        issue.StatusChanges = history
            .Where(change => change.At > created && change.At <= now)
            .OrderBy(change => change.At)
            .Select(change => new IssueStatusChange
            {
                Issue = issue,
                FromStatus = change.From,
                ToStatus = change.To,
                ChangedAt = change.At
            })
            .ToList();
    }
}
