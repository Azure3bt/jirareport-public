namespace JiraReporter.Api.Domain.Entities;

public sealed class JiraUser
{
    public string AccountId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? EmailAddress { get; set; }
}
