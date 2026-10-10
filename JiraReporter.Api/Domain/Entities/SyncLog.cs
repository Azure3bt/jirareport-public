namespace JiraReporter.Api.Domain.Entities;

public sealed class SyncLog
{
    public long Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int IssuesSynced { get; set; }
    public string? Error { get; set; }
}
