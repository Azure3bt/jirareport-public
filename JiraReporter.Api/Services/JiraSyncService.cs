using System.Text.Json;
using System.Text.RegularExpressions;
using JiraReporter.Api.Data;
using JiraReporter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JiraReporter.Api.Services;

public class JiraSyncService(
    JiraClient jiraClient,
    AppDbContext dbContext,
    IConfiguration configuration,
    ILogger<JiraSyncService> logger)
{
    private static readonly Regex sprintNameRegex = new(@"name=([^,\]]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<int> SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var syncLog = new SyncLog
        {
            StartedAt = DateTime.UtcNow,
            IssuesSynced = 0
        };

        dbContext.SyncLogs.Add(syncLog);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var jql = configuration["Jira:Jql"] ?? "ORDER BY updated DESC";
            var pageSize = configuration.GetValue("Jira:PageSize", 50);
            var totalSynced = 0;

            // استفاده دقیق از متد SearchAsync کلاینت شما با الگوی Callback
            await jiraClient.SearchAsync(
                jql,
                pageSize,
                async doc =>
                {
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("issues", out var issuesElement) || issuesElement.ValueKind != JsonValueKind.Array)
                    {
                        return;
                    }

                    foreach (var issueElement in issuesElement.EnumerateArray())
                    {
                        await UpsertIssueAsync(issueElement, cancellationToken);
                        if (issueElement.TryGetProperty("key", out var issueKeyElement) && issueKeyElement.GetString() is { Length: > 0 } issueKey)
                            await SyncWorklogsAsync(issueKey, cancellationToken);
                        totalSynced++;
                    }

                    await dbContext.SaveChangesAsync(cancellationToken);
                },
                cancellationToken);

            syncLog.FinishedAt = DateTime.UtcNow;
            syncLog.IssuesSynced = totalSynced;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Jira sync completed successfully. Synced {Count} issues.", totalSynced);
            return totalSynced;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Jira sync failed.");
            syncLog.FinishedAt = DateTime.UtcNow;
            syncLog.Error = ex.Message;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private async Task SyncWorklogsAsync(string issueKey, CancellationToken cancellationToken)
    {
        var worklogs = await jiraClient.GetWorklogsAsync(issueKey, cancellationToken);
        foreach (var item in worklogs)
        {
            if (string.IsNullOrWhiteSpace(item.WorklogId)) continue;
            if (!string.IsNullOrWhiteSpace(item.AccountId))
            {
                var user = await dbContext.JiraUsers.FindAsync([item.AccountId], cancellationToken);
                if (user is null)
                    dbContext.JiraUsers.Add(new JiraUser { AccountId = item.AccountId, DisplayName = item.DisplayName, EmailAddress = item.EmailAddress });
                else
                {
                    user.DisplayName = item.DisplayName;
                    user.EmailAddress = item.EmailAddress;
                }
            }

            var log = await dbContext.Worklogs.FindAsync([item.WorklogId], cancellationToken);
            if (log is null)
                dbContext.Worklogs.Add(new Worklog { WorklogId = item.WorklogId, IssueKey = item.IssueKey, AuthorAccountId = item.AccountId, Started = item.Started, TimeSpentSeconds = item.TimeSpentSeconds, Comment = item.Comment });
            else
            {
                log.IssueKey = item.IssueKey;
                log.AuthorAccountId = item.AccountId;
                log.Started = item.Started;
                log.TimeSpentSeconds = item.TimeSpentSeconds;
                log.Comment = item.Comment;
            }
        }
    }

    private async Task UpsertIssueAsync(JsonElement element, CancellationToken cancellationToken)
    {
        if (!element.TryGetProperty("key", out var keyElement))
        {
            return;
        }

        var key = keyElement.GetString();
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var issue = await dbContext.Issues
            .Include(i => i.StatusChanges)
            .FirstOrDefaultAsync(i => i.Key == key, cancellationToken);

        if (issue == null)
        {
            issue = new Issue { Key = key };
            dbContext.Issues.Add(issue);
        }

        UpdateIssueFields(issue, element);

        // اطمینان از وجود پروژه در جدول Projects
        if (!string.IsNullOrWhiteSpace(issue.ProjectKey))
        {
            var projectExists = await dbContext.Projects.AnyAsync(p => p.IssueKey == issue.ProjectKey, cancellationToken);
            if (!projectExists)
            {
                dbContext.Projects.Add(new Project
                {
                    IssueKey = issue.ProjectKey,
                    Name = issue.ProjectKey
                });
            }
        }

        // سینک تاریخچه ترنزیشن‌ها (StatusChanges) برای گزارش Rework و چرخه کاری
        SyncStatusHistory(issue, element);
    }

    private void UpdateIssueFields(Issue issue, JsonElement element)
    {
        if (!element.TryGetProperty("fields", out var fields))
        {
            return;
        }

        issue.Summary = ReadString(fields, "summary") ?? issue.Key;
        issue.Type = fields.TryGetProperty("issuetype", out var it) ? ReadString(it, "name") ?? "Task" : "Task";
        issue.Status = fields.TryGetProperty("status", out var st) ? ReadString(st, "name") ?? "To Do" : "To Do";

        issue.Assignee = fields.TryGetProperty("assignee", out var asg) ? ReadString(asg, "displayName") : null;
        issue.Reporter = fields.TryGetProperty("reporter", out var rep) ? ReadString(rep, "displayName") : null;

        issue.Created = ReadDateTime(fields, "created") ?? DateTime.UtcNow;
        issue.Updated = ReadDateTime(fields, "updated") ?? issue.Created;

        // محاسبه تاریخ تکمیل (Resolved)
        issue.Resolved = ReadDateTime(fields, "resolutiondate");
        if (issue.Resolved == null && IsResolvedStatus(issue.Status, fields))
        {
            issue.Resolved = ReadDateTime(fields, "statuscategorychangedate") ?? issue.Updated;
        }

        if (fields.TryGetProperty("project", out var proj))
        {
            issue.ProjectKey = ReadString(proj, "key") ?? "";
        }

        // خواندن Story Points با اولویت کانفیگ، و سپس customfield_10046 جیرای شما
        var spField = configuration["Jira:StoryPointsField"];
        issue.StoryPoints = ReadNumber(fields, spField)
            ?? ReadNumber(fields, "customfield_10046")
            ?? ReadNumber(fields, "customfield_10016");

        // استخراج نام Sprint
        var sprintField = configuration["Jira:SprintField"];
        var sprintName = ReadSprintName(fields, sprintField)
            ?? ReadSprintName(fields, "customfield_10020")
            ?? ReadSprintName(fields, "customfield_10001");

        if (!string.IsNullOrWhiteSpace(sprintName))
        {
            issue.SprintName = sprintName;
        }
    }

    private void SyncStatusHistory(Issue issue, JsonElement element)
    {
        // حذف و بازسازی کامل تغییرات وضعیت
        issue.StatusChanges.Clear();

        var changes = new List<IssueStatusChange>();

        if (element.TryGetProperty("changelog", out var changelog) &&
            changelog.TryGetProperty("histories", out var histories) &&
            histories.ValueKind == JsonValueKind.Array)
        {
            foreach (var history in histories.EnumerateArray())
            {
                var changeDate = ReadDateTime(history, "created") ?? issue.Updated;

                if (!history.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in items.EnumerateArray())
                {
                    var field = ReadString(item, "field");
                    if (string.Equals(field, "status", StringComparison.OrdinalIgnoreCase))
                    {
                        var fromStatus = ReadString(item, "fromString") ?? "";
                        var toStatus = ReadString(item, "toString") ?? "";

                        changes.Add(new IssueStatusChange
                        {
                            Issue = issue,
                            IssueId = issue.Id,
                            ChangedAt = changeDate,
                            FromStatus = fromStatus,
                            ToStatus = toStatus
                        });
                    }
                }
            }
        }

        if (changes.Count > 0)
        {
            foreach (var ch in changes.OrderBy(c => c.ChangedAt))
            {
                issue.StatusChanges.Add(ch);
            }
        }
        else
        {
            // در صورت عدم وجود سابقه در لاگ، وضعیت اولیه را ثبت کن
            issue.StatusChanges.Add(new IssueStatusChange
            {
                Issue = issue,
                IssueId = issue.Id,
                ChangedAt = issue.Created,
                FromStatus = "Created",
                ToStatus = issue.Status
            });
        }
    }

    private static string? ReadSprintName(JsonElement fields, string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName) || !fields.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        return ParseSprintElement(element);
    }

    private static string? ParseSprintElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                string? lastFound = null;
                foreach (var item in element.EnumerateArray())
                {
                    var name = ParseSprintElement(item);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        lastFound = name;
                    }
                }
                return lastFound;

            case JsonValueKind.Object:
                if (element.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                {
                    return nameProp.GetString();
                }
                break;

            case JsonValueKind.String:
                var str = element.GetString();
                if (string.IsNullOrWhiteSpace(str))
                    return null;

                var match = sprintNameRegex.Match(str);
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim();
                }
                return str;
        }

        return null;
    }

    private static bool IsResolvedStatus(string status, JsonElement fields)
    {
        if (string.Equals(status, "Done", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Closed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Resolved", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (fields.TryGetProperty("status", out var statusObj) &&
            statusObj.TryGetProperty("statusCategory", out var category) &&
            category.TryGetProperty("key", out var keyProp))
        {
            return string.Equals(keyProp.GetString(), "done", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string? ReadString(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    private static double? ReadNumber(JsonElement element, string? propName)
    {
        if (string.IsNullOrWhiteSpace(propName) || !element.TryGetProperty(propName, out var prop))
        {
            return null;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var d))
        {
            return d;
        }

        if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateTime? ReadDateTime(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(prop.GetString(), out var dt))
            {
                return dt.ToUniversalTime();
            }
        }
        return null;
    }
}
