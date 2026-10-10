using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JiraReporter.Api.Services;

public sealed record JiraWorklogData(string WorklogId, string IssueKey, string AccountId, string DisplayName, string? EmailAddress, DateTime Started, int TimeSpentSeconds, string? Comment);

public sealed class JiraClient(HttpClient http, IConfiguration configuration, ILogger<JiraClient> logger)
{
    private readonly string _baseUrl = (configuration["Jira:BaseUrl"] ?? string.Empty).TrimEnd('/');

    public async Task SearchAsync(
        string jql,
        int pageSize,
        Func<JsonDocument, Task> onPage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jql);
        ArgumentNullException.ThrowIfNull(onPage);
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be greater than zero.");
        if (string.IsNullOrWhiteSpace(_baseUrl))
            throw new InvalidOperationException("Jira:BaseUrl must be configured.");

        var credentials = $"{configuration["Jira:Email"]}:{configuration["Jira:ApiToken"]}";
        var encodedCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
        var authorization = new AuthenticationHeaderValue("Basic", encodedCredentials);
        string? nextPageToken = null;
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);

        while (true)
        {
            var url = $"{_baseUrl}/rest/api/3/search/jql?{BuildQuery(jql, pageSize, nextPageToken, includeAllFields: true)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = authorization;
            using var response = await http.SendAsync(request, cancellationToken);

            JsonDocument document;
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Jira search failed with HTTP {StatusCode}. URL: {Url}. Response: {Response}",
                    (int)response.StatusCode, url, errorText);
                logger.LogWarning("Retrying Jira search without fields=*all.");

                var retryUrl = $"{_baseUrl}/rest/api/3/search/jql?{BuildQuery(jql, pageSize, nextPageToken, includeAllFields: false)}";
                using var retryRequest = new HttpRequestMessage(HttpMethod.Get, retryUrl);
                retryRequest.Headers.Authorization = authorization;
                using var retryResponse = await http.SendAsync(retryRequest, cancellationToken);
                if (!retryResponse.IsSuccessStatusCode)
                {
                    var retryError = await retryResponse.Content.ReadAsStringAsync(cancellationToken);
                    logger.LogError("Jira search retry failed with HTTP {StatusCode}. URL: {Url}. Response: {Response}",
                        (int)retryResponse.StatusCode, retryUrl, retryError);
                    retryResponse.EnsureSuccessStatusCode();
                }
                document = await ReadPageAsync(retryResponse, cancellationToken);
            }
            else
            {
                if (!response.IsSuccessStatusCode)
                {
                    var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
                    logger.LogError("Jira search failed with HTTP {StatusCode}. URL: {Url}. Response: {Response}",
                        (int)response.StatusCode, url, errorText);
                    response.EnsureSuccessStatusCode();
                }
                document = await ReadPageAsync(response, cancellationToken);
            }

            using (document)
            {
                var root = document.RootElement;
                if (!root.TryGetProperty("issues", out var issues) || issues.ValueKind != JsonValueKind.Array)
                    throw new JsonException("Jira search response did not contain an issues array.");

                await onPage(document);
                if (root.TryGetProperty("isLast", out var isLast) && isLast.ValueKind == JsonValueKind.True)
                    break;
                if (!root.TryGetProperty("nextPageToken", out var tokenValue) ||
                    tokenValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(tokenValue.GetString()))
                    break;

                nextPageToken = tokenValue.GetString()!;
                if (!seenTokens.Add(nextPageToken))
                    throw new InvalidOperationException("Jira search returned a repeated nextPageToken; stopping to avoid an infinite loop.");
            }
        }
    }

    public async Task<IReadOnlyList<JiraWorklogData>> GetWorklogsAsync(string issueKey, CancellationToken cancellationToken = default)
    {
        var credentials = $"{configuration["Jira:Email"]}:{configuration["Jira:ApiToken"]}";
        var authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        var result = new List<JiraWorklogData>();
        var startAt = 0;
        const int pageSize = 100;
        while (true)
        {
            var url = $"{_baseUrl}/rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/worklog?startAt={startAt}&maxResults={pageSize}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = authorization;
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            if (!root.TryGetProperty("worklogs", out var logs) || logs.ValueKind != JsonValueKind.Array) break;
            foreach (var item in logs.EnumerateArray())
            {
                var author = item.TryGetProperty("author", out var authorElement) ? authorElement : default;
                result.Add(new JiraWorklogData(
                    item.TryGetProperty("id", out var id) ? id.ToString() : string.Empty,
                    issueKey,
                    ReadString(author, "accountId") ?? string.Empty,
                    ReadString(author, "displayName") ?? "نامشخص",
                    ReadString(author, "emailAddress"),
                    item.TryGetProperty("started", out var started) && DateTime.TryParse(started.GetString(), out var date) ? date.ToUniversalTime() : DateTime.UtcNow,
                    item.TryGetProperty("timeSpentSeconds", out var seconds) && seconds.TryGetInt32(out var value) ? value : 0,
                    item.TryGetProperty("comment", out var comment) ? comment.ToString() : null));
            }
            startAt += logs.GetArrayLength();
            if (logs.GetArrayLength() == 0 || !root.TryGetProperty("total", out var total) || startAt >= total.GetInt32()) break;
        }
        return result;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private string BuildQuery(string jql, int pageSize, string? nextPageToken, bool includeAllFields)
    {
        var query = new StringBuilder()
            .Append("jql=").Append(Uri.EscapeDataString(jql))
            .Append("&maxResults=").Append(pageSize);
        if (includeAllFields)
            query.Append("&fields=").Append(Uri.EscapeDataString("*all"));
        query.Append("&expand=").Append(Uri.EscapeDataString("changelog"));
        if (!string.IsNullOrEmpty(nextPageToken))
            query.Append("&nextPageToken=").Append(Uri.EscapeDataString(nextPageToken));
        return query.ToString();
    }

    private async Task<JsonDocument> ReadPageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(content);
    }
}
