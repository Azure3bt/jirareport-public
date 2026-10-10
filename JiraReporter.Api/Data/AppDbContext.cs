using JiraReporter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace JiraReporter.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Issue> Issues => Set<Issue>();
    public DbSet<IssueStatusChange> StatusChanges => Set<IssueStatusChange>();
    public DbSet<SyncLog> SyncLogs => Set<SyncLog>();
    public DbSet<JiraUser> JiraUsers => Set<JiraUser>();
    public DbSet<Worklog> Worklogs => Set<Worklog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>().HasKey(project => project.IssueKey);
        modelBuilder.Entity<Issue>().HasIndex(issue => issue.Key).IsUnique();
        modelBuilder.Entity<Issue>().Property(issue => issue.Key).IsRequired();
        modelBuilder.Entity<JiraUser>().HasKey(user => user.AccountId);
        modelBuilder.Entity<Worklog>().HasKey(worklog => worklog.WorklogId);
        modelBuilder.Entity<Worklog>().HasIndex(worklog => worklog.AuthorAccountId);
        modelBuilder.Entity<Worklog>().HasIndex(worklog => worklog.IssueKey);
        modelBuilder.Entity<IssueStatusChange>().HasIndex(change => change.IssueId);
        modelBuilder.Entity<Issue>()
            .HasMany(issue => issue.StatusChanges)
            .WithOne(change => change.Issue)
            .HasForeignKey(change => change.IssueId)
            .OnDelete(DeleteBehavior.Cascade);

        base.OnModelCreating(modelBuilder);
    }
}
