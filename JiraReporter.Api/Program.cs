using JiraReporter.Api.Data;
using JiraReporter.Api.Hosted;
using JiraReporter.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHttpClient<JiraClient>();
builder.Services.AddScoped<JiraSyncService>();
builder.Services.AddHostedService<SyncWorker>();
builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

await ApplyDatabaseMigrationsAsync(app.Services);

if (app.Configuration.GetValue<bool>("Jira:SeedOnStartup"))
    await global::JiraSeeder.SeedData(app.Configuration);

app.UseSwagger();
app.UseSwaggerUI();
app.UseStaticFiles();
app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");
app.Run();

static async Task ApplyDatabaseMigrationsAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await database.Database.EnsureCreatedAsync();
    await database.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "JiraUsers" (
            "AccountId" TEXT NOT NULL CONSTRAINT "PK_JiraUsers" PRIMARY KEY,
            "DisplayName" TEXT NOT NULL,
            "EmailAddress" TEXT NULL
        );
        CREATE TABLE IF NOT EXISTS "Worklogs" (
            "WorklogId" TEXT NOT NULL CONSTRAINT "PK_Worklogs" PRIMARY KEY,
            "IssueKey" TEXT NOT NULL,
            "AuthorAccountId" TEXT NOT NULL,
            "Started" TEXT NOT NULL,
            "TimeSpentSeconds" INTEGER NOT NULL,
            "Comment" TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_Worklogs_AuthorAccountId" ON "Worklogs" ("AuthorAccountId");
        CREATE INDEX IF NOT EXISTS "IX_Worklogs_IssueKey" ON "Worklogs" ("IssueKey");
        """);
    await JiraDataInitializer.SeedAsync(database);

}
