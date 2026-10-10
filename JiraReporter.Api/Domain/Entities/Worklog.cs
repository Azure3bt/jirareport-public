namespace JiraReporter.Api.Domain.Entities;

public sealed class Worklog
{
    public string WorklogId { get; set; } = string.Empty;
    public string IssueKey { get; set; } = string.Empty;
    public string AuthorAccountId { get; set; } = string.Empty;
    public DateTime Started { get; set; }
    public int TimeSpentSeconds { get; set; }
    public string? Comment { get; set; }
}
