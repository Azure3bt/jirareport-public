namespace JiraReporter.Api.Domain.Entities;

public sealed class Project
{
    public string IssueKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
