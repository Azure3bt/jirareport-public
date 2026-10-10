// JiraSeeder.cs — Jira Cloud REST API v3 sample seeder (.NET 8+)
// Required configuration: Jira:BaseUrl, Jira:Email, Jira:ApiToken, Jira:ProjectKey
// Optional configuration: Jira:BoardId, Jira:TransitionDelaySeconds
// Story-point and sprint fields are discovered automatically where possible.
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class JiraSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static string BaseUrl = string.Empty;
    private static string? StoryPointsField;
    private static string? SelfAccountId;
    private static readonly List<JiraUser> Users = [];
    private static int TransitionDelaySeconds;

    public static async Task SeedData(IConfiguration configuration)
    {
        try
        {
            BaseUrl = Required(configuration["Jira:BaseUrl"], "Jira:BaseUrl").TrimEnd('/') + "/";
            var email = Required(configuration["Jira:Email"], "Jira:Email");
            var token = Required(configuration["Jira:ApiToken"], "Jira:ApiToken");
            var project = Required(configuration["Jira:ProjectKey"], "Jira:ProjectKey");
            var boardSetting = configuration["Jira:BoardId"];
            TransitionDelaySeconds = int.TryParse(configuration["Jira:TransitionDelaySeconds"], out var delay)
                ? Math.Clamp(delay, 0, 30) : 0;

            using var http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromMinutes(3) };
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{token}"));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var boardId = await ResolveBoardId(http, project, boardSetting);
            Console.WriteLine($"Using Scrum board {boardId} for project {project}.");
            await DiscoverFields(http);
            await DiscoverUsers(http, project);

            var existingSeeds = await CountExistingSeeds(http, project);
            if (existingSeeds > 0)
            {
                Console.WriteLine($"Found {existingSeeds} Jira Reporter seed issues in {project}; skipping to avoid duplicates. Remove the jira-reporter-seed issues before seeding again.");
                return;
            }

            var sprintNames = new[]
            {
                "Sprint 01 - Foundation", "Sprint 02 - Payments",
                "Sprint 03 - Reliability", "Sprint 04 - Analytics"
            };
            var sprintIds = new[]
            {
                await CreateSprint(http, boardId, sprintNames[0], "Completed sample work", -30, -16),
                await CreateSprint(http, boardId, sprintNames[1], "Active sample work", 0, 14),
                await CreateSprint(http, boardId, sprintNames[2], "Upcoming sample work", 15, 29),
                await CreateSprint(http, boardId, sprintNames[3], "Upcoming sample work", 30, 44)
            };

            await SprintAction(http, sprintIds[0], "start");
            var specs = BuildSpecs();
            var created = 0;
            foreach (var spec in specs)
            {
                var key = await CreateIssue(http, project, spec);
                if (spec.SprintIndex >= 0)
                    await AddToSprint(http, sprintIds[spec.SprintIndex], key);
                await AssignIssue(http, key, spec.Assignee);
                await ApplyLifecycle(http, key, spec);
                Console.WriteLine($"Created {key}: {spec.Type}, {spec.Points} SP, {spec.SprintName}, target={spec.Status}");
                created++;
            }

            await SprintAction(http, sprintIds[0], "complete");
            await SprintAction(http, sprintIds[1], "start");

            Console.WriteLine($"Finished: {created} issues across four sprints.");
            Console.WriteLine("Jira REST assigns real creation and transition timestamps; historical dates from the SQLite seeder cannot be copied.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Seeder stopped: " + ex.Message);
            Environment.ExitCode = 1;
        }
    }

    private static string Required(string? value, string name) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidOperationException($"Set configuration value {name}.");

    private static async Task<int> CountExistingSeeds(HttpClient http, string project)
    {
        string? nextPageToken = null;
        var count = 0;
        do
        {
            var url = $"rest/api/3/search/jql?jql={Uri.EscapeDataString($"project = {project} AND labels = \"jira-reporter-seed\"")}&maxResults=100&fields=labels";
            if (!string.IsNullOrWhiteSpace(nextPageToken))
                url += $"&nextPageToken={Uri.EscapeDataString(nextPageToken)}";
            using var page = await Send(http, HttpMethod.Get, url);
            var root = page.RootElement;
            if (root.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Array)
                count += issues.GetArrayLength();
            nextPageToken = root.TryGetProperty("nextPageToken", out var token) && token.ValueKind == JsonValueKind.String
                ? token.GetString() : null;
        } while (!string.IsNullOrWhiteSpace(nextPageToken));
        return count;
    }

    private static List<SeedIssue> BuildSpecs()
    {
        string[] areas = ["Payment gateway", "Customer portal", "Reporting pipeline", "Identity service", "Mobile checkout",
            "Notification worker", "Ledger reconciliation", "Search indexing", "Admin dashboard", "Data export"];
        string[] actions = ["Add retry handling", "Improve validation", "Investigate intermittent failure", "Refactor request flow",
            "Add audit logging", "Reduce response latency", "Support bulk operations", "Improve error messaging",
            "Add monitoring metrics", "Update integration tests", "Fix edge-case calculation", "Document recovery steps"];
        string[] assignees = ["Alex Morgan", "Samira Ahmadi", "Jordan Lee", "Nima Karimi", "Taylor Kim", "Roya Hashemi"];
        string[] sprints = ["Sprint 01 - Foundation", "Sprint 02 - Payments", "Sprint 03 - Reliability", "Sprint 04 - Analytics"];
        double[] points = [1, 2, 3, 5, 8, 13];
        var specs = new List<SeedIssue>(120);
        for (var i = 0; i < 120; i++)
        {
            var state = i % 12;
            var status = state switch
            {
                0 or 1 or 5 or 11 => "Done", 2 or 6 => "In Progress", 3 or 10 => "In Review",
                7 => "Closed", 8 => "Canceled", _ => "To Do"
            };
            var remainder = i % 5;
            var type = remainder is 0 or 4 ? "Bug" : remainder is 1 or 2 ? "Story" : "Task";
            var sprintIndex = i % 7 == 0 ? -1 : (i / 3 + i % 4) % 4;
            var assignee = i % 11 == 0 ? null : assignees[(i * 3 + i / 5) % assignees.Length];
            var storyPoints = type == "Bug" ? points[(i + 1) % 4] : points[(i * 5 + 2) % points.Length];
            specs.Add(new SeedIssue(i, $"{areas[i % areas.Length]}: {actions[(i * 7 + i / 3) % actions.Length]} #{i + 1:000}",
                type, (int)storyPoints, sprintIndex, sprintIndex < 0 ? null : sprints[sprintIndex], status, assignee));
        }
        return specs;
    }

    private static async Task<int> ResolveBoardId(HttpClient http, string project, string? configured)
    {
        if (int.TryParse(configured, out var boardId) && boardId > 0)
            return boardId;
        using var response = await Send(http, HttpMethod.Get,
            $"rest/agile/1.0/board?projectKeyOrId={Uri.EscapeDataString(project)}&type=scrum&maxResults=50");
        var boards = response.RootElement.GetProperty("values").EnumerateArray().ToList();
        if (boards.Count == 0)
            throw new InvalidOperationException($"No Scrum board found for {project}. Create a Scrum board or set JIRA_BOARD_ID.");
        var exact = boards.FirstOrDefault(b => b.TryGetProperty("location", out var loc) &&
            loc.TryGetProperty("projectKey", out var key) && key.GetString()?.Equals(project, StringComparison.OrdinalIgnoreCase) == true);
        var selected = exact.ValueKind == JsonValueKind.Object ? exact : boards[0];
        var id = selected.GetProperty("id").GetInt32();
        Console.WriteLine($"JIRA_BOARD_ID missing/0; discovered board '{selected.GetProperty("name").GetString()}' ({id}).");
        return id;
    }

    private static async Task DiscoverFields(HttpClient http)
    {
        using var fields = await Send(http, HttpMethod.Get, "rest/api/3/field");
        foreach (var field in fields.RootElement.EnumerateArray())
        {
            var name = GetString(field, "name");
            if (name.Equals("Story Points", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Story point estimate", StringComparison.OrdinalIgnoreCase))
                StoryPointsField ??= GetString(field, "id");
        }
        Console.WriteLine(StoryPointsField is null ? "Warning: Story Points field not found; estimates omitted." : $"Story Points field: {StoryPointsField}");
    }

    private static async Task DiscoverUsers(HttpClient http, string project)
    {
        try
        {
            using var me = await Send(http, HttpMethod.Get, "rest/api/3/myself");
            SelfAccountId = GetString(me.RootElement, "accountId");
        }
        catch (HttpRequestException ex) { Console.WriteLine($"Could not look up current user: {ex.Message}"); }
        try
        {
            using var found = await Send(http, HttpMethod.Get,
                $"rest/api/3/user/assignable/search?project={Uri.EscapeDataString(project)}&maxResults=50");
            foreach (var user in found.RootElement.EnumerateArray())
            {
                var id = GetString(user, "accountId");
                var name = GetString(user, "displayName");
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                    Users.Add(new JiraUser(id, name));
            }
        }
        catch (HttpRequestException ex) { Console.WriteLine($"Assignee search unavailable; named assignees may remain unassigned: {ex.Message}"); }
        Console.WriteLine($"Assignable users discovered: {Users.Count}; self assignment available: {!string.IsNullOrEmpty(SelfAccountId)}.");
    }

    private static async Task<int> CreateSprint(HttpClient http, int boardId, string name, string goal, int startOffset, int endOffset)
    {
        var start = DateTimeOffset.UtcNow.Date.AddDays(startOffset).ToString("yyyy-MM-dd'T'00:00:00.000+0000");
        var end = DateTimeOffset.UtcNow.Date.AddDays(endOffset).ToString("yyyy-MM-dd'T'00:00:00.000+0000");
        using var doc = await Send(http, HttpMethod.Post, "rest/agile/1.0/sprint",
            new
            {
                name,
                goal,
                originBoardId = boardId,
                startDate = start,
                endDate = end
            });
        return doc.RootElement.GetProperty("id").GetInt32();
    }

    private static async Task SprintAction(HttpClient http, int sprintId, string action)
    {
        // در جیرا شروع اسپرینت با state="active" و بستن آن با state="closed" انجام می‌شود
        var targetState = action.ToLowerInvariant() switch
        {
            "start" => "active",
            "complete" or "close" => "closed",
            _ => action
        };

        var body = new Dictionary<string, object>
        {
            ["state"] = targetState
        };

        // اگر اسپرینت قرار است بسته شود، معمولاً Jira نیاز به completeDate دارد
        if (targetState == "closed")
        {
            body["completeDate"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz");
        }

        _ = await Send(http, HttpMethod.Post, $"rest/agile/1.0/sprint/{sprintId}", body);
        Console.WriteLine($"Sprint {sprintId}: state updated to '{targetState}'.");
    }


    private static async Task<string> CreateIssue(HttpClient http, string project, SeedIssue spec)
    {
        var fields = new Dictionary<string, object>
        {
            ["project"] = new { key = project },
            ["summary"] = spec.Summary,
            ["issuetype"] = new { name = spec.Type },
            ["description"] = new
            {
                type = "doc",
                version = 1,
                content = new object[]
                {
                new
                {
                    type = "paragraph",
                    content = new object[]
                    {
                        new { type = "text", text = $"Seeded reporting sample — {spec.SprintName ?? "Backlog"}; {spec.Points} story points." }
                    }
                }
                }
            }
        };

        fields["labels"] = new[] { "jira-reporter-seed" };

        // اگر فیلد پیدا شده باشد، ابتدا آن را اضافه می‌کنیم
        if (spec.Points > 0 && StoryPointsField is not null)
        {
            fields[StoryPointsField] = spec.Points;
        }

        try
        {
            using var doc = await Send(http, HttpMethod.Post, "rest/api/3/issue", new
            {
                fields
            });
            return GetString(doc.RootElement, "key");
        }
        catch (HttpRequestException ex) when (ex.Message.Contains(StoryPointsField ?? "____"))
        {
            // اگر جیرا گفت فیلد روی Create Screen نیست، تسک را بدون فیلد می‌سازیم
            fields.Remove(StoryPointsField!);
            using var fallbackDoc = await Send(http, HttpMethod.Post, "rest/api/3/issue", new
            {
                fields
            });
            var key = GetString(fallbackDoc.RootElement, "key");
            Console.WriteLine($"Created {key} without Story Points field (not on Create Screen).");
            return key;
        }
    }


    private static async Task AssignIssue(HttpClient http, string key, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return;
        var chosen = Users.FirstOrDefault(user => user.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase))?.AccountId;
        if (string.IsNullOrWhiteSpace(chosen))
        {
            Console.WriteLine($"No assignable Jira account matches '{displayName}'; leaving issue unassigned.");
            return;
        }
        try
        {
            _ = await Send(http, HttpMethod.Put, $"rest/api/3/issue/{key}/assignee", new
            {
                accountId = chosen
            });
        }
        catch (HttpRequestException ex) { Console.WriteLine($"Could not assign {key}; leaving current assignee: {ex.Message}"); }
    }

    private static async Task AddToSprint(HttpClient http, int sprintId, string issueKey) =>
        _ = await Send(http, HttpMethod.Post, $"rest/agile/1.0/sprint/{sprintId}/issue", new
        {
            issues = new[] { issueKey }
        });

    private static async Task ApplyLifecycle(HttpClient http, string key, SeedIssue spec)
    {
        if (spec.Status == "To Do") return;
        await TransitionToward(http, key, "In Progress");
        if (spec.Status is "In Review" or "Done" or "Closed" or "Canceled")
            await TransitionToward(http, key, "In Review");
        if (spec.Status is "Done" or "Closed" or "Canceled" && spec.Index % 3 == 1)
            await TransitionToward(http, key, "In Progress");
        if (spec.Status is "Done" or "Closed" or "Canceled")
            await TransitionToward(http, key, spec.Status);
    }

    private static async Task TransitionToward(HttpClient http, string key, string targetStatus)
    {
        var ok = await TryTransitionToward(http, key, targetStatus);
        if (!ok)
        {
            Console.WriteLine($"Could not transition {key} to '{targetStatus}'. Workflow may not allow this direct step.");
        }
    }

    private static async Task<bool> TryTransitionToward(HttpClient http, string key, string targetStatus)
    {
        if (TransitionDelaySeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(TransitionDelaySeconds));
        }

        using var transDoc = await Send(http, HttpMethod.Get, $"rest/api/3/issue/{key}/transitions");
        var transitions = transDoc.RootElement.GetProperty("transitions").EnumerateArray();

        string? transitionId = null;
        foreach (var t in transitions)
        {
            var name = t.GetProperty("name").GetString();
            var toStatus = t.GetProperty("to").GetProperty("name").GetString();
            if (string.Equals(name, targetStatus, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(toStatus, targetStatus, StringComparison.OrdinalIgnoreCase))
            {
                transitionId = t.GetProperty("id").GetString();
                break;
            }
        }

        if (transitionId is null)
            return false;

        _ = await Send(http, HttpMethod.Post, $"rest/api/3/issue/{key}/transitions", new
        {
            transition = new
            {
                id = transitionId
            }
        });

        return true;
    }

    private static async Task<JsonDocument> Send(HttpClient http, HttpMethod method, string relativeUrl, object? body = null)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Jira API {method} {relativeUrl} failed ({(int)response.StatusCode} {response.StatusCode}): {content}");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return JsonDocument.Parse("{}");
        }

        return JsonDocument.Parse(content);
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString() ?? "";
        }
        return "";
    }


    private sealed record SeedIssue(int Index, string Summary, string Type, int Points, int SprintIndex, string? SprintName, string Status, string? Assignee);
    private sealed record JiraUser(string AccountId, string DisplayName);
}
