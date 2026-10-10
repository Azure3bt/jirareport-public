namespace JiraReporter.Api.Domain.Entities;

public sealed class IssueStatusChange
{
    public long Id { get; set; }
    public long IssueId { get; set; }
    public Issue Issue { get; set; } = null!;
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public DateTime ChangedAt { get; set; }
}
