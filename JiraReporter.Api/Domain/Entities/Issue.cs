namespace JiraReporter.Api.Domain.Entities;

public sealed class Issue
{
    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Assignee { get; set; }
    public string? Reporter { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public DateTime? Resolved { get; set; }
    public double? StoryPoints { get; set; }
    public string? SprintName { get; set; }
    public string ProjectKey { get; set; } = string.Empty;
    public List<IssueStatusChange> StatusChanges { get; set; } = [];
}
