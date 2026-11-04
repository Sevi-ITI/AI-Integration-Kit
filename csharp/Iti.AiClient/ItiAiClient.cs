using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Iti.AiClient;

// Calls the ITI AI API v1. Register once in Program.cs with AddHttpClient<ItiAiClient>(...), setting
// BaseAddress, Timeout (150 s: answers can queue for 120 s) and the "ITI-Api-Key" header (server-side only).
// userId = the signed-in person's permanent id from YOUR server session, never from the browser.
public sealed class ItiAiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    // Usage on the reply: the person's daily share and the key's month, from the headers (v1.1).
    public async Task<ChatResponse> ChatAsync(
        string userId, ChatRequest request, string? userRole = null, CancellationToken ct = default)
    {
        using var message = ForUser(HttpMethod.Post, "v1/chat", userId, userRole, request);
        using var response = await http.SendAsync(message, ct);
        await ThrowIfErrorAsync(response, ct);
        var reply = (await response.Content.ReadFromJsonAsync<ChatResponse>(Json, ct))!;
        return reply with { Usage = UsageFrom(response) };
    }

    // Refusals before the stream starts (401, 403, 404, 422, 429) and "error" events both throw ItiAiException.
    // The first event ("meta") carries Usage, from the stream's headers.
    public async IAsyncEnumerable<ChatStreamEvent> ChatStreamAsync(
        string userId, ChatRequest request, string? userRole = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var message = ForUser(HttpMethod.Post, "v1/chat/stream", userId, userRole, request);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        await ThrowIfErrorAsync(response, ct);
        var usage = UsageFrom(response);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        string kind = "", requestId = "";
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            // SSE: "event: x" then "data: {json}"; blank lines and ": ping" keep-alives are skipped
            if (line.StartsWith("event:")) kind = line[6..].Trim();
            else if (line.StartsWith("data:"))
            {
                if (kind == "error")
                {
                    var error = JsonNode.Parse(line[5..])!;
                    throw new ItiAiException(0, (string)error["code"]!, (string)error["message"]!, requestId);
                }
                var ev = JsonSerializer.Deserialize<ChatStreamEvent>(line[5..], Json)! with { Kind = kind, Usage = usage };
                usage = null;  // only the first event carries it
                requestId = ev.RequestId ?? requestId;
                yield return ev;
            }
        }
    }

    public Task<List<ConversationSummary>> GetConversationsAsync(string userId, int limit = 30, CancellationToken ct = default) =>
        SendAsync<List<ConversationSummary>>(ForUser(HttpMethod.Get, $"v1/conversations?limit={limit}", userId), ct);

    public Task<List<MessageOut>> GetMessagesAsync(string userId, string conversationId, CancellationToken ct = default) =>
        SendAsync<List<MessageOut>>(
            ForUser(HttpMethod.Get, $"v1/conversations/{Uri.EscapeDataString(conversationId)}/messages", userId), ct);

    // This person's model tokens today through this key, and their daily limit (null = no limit). For a meter bar.
    public Task<UsageMe> GetUsageMeAsync(string userId, CancellationToken ct = default) =>
        SendAsync<UsageMe>(ForUser(HttpMethod.Get, "v1/usage/me", userId), ct);

    // This key's tokens this month and its limit (v1.2). No userId: it is the whole key, every person together,
    // so show it to your admins only. Same numbers as Usage.TokensRemaining / TokensLimit on every answer.
    public Task<UsageKey> GetUsageKeyAsync(CancellationToken ct = default) =>
        SendAsync<UsageKey>(new HttpRequestMessage(HttpMethod.Get, "v1/usage/key"), ct);

    // Starter questions (chips) for a new chat, one per file, picked at random. A chip click fills the question box.
    public Task<List<Suggestion>> GetSuggestionsAsync(string collection, int limit = 3, CancellationToken ct = default) =>
        SendAsync<List<Suggestion>>(new HttpRequestMessage(
            HttpMethod.Get, $"v1/suggestions?collection={Uri.EscapeDataString(collection)}&limit={limit}"), ct);

    // Needs documents:write. Code "document_exists" -> ask "Replace the existing file?", then call again with replace: true.
    // Indexing runs after the reply: poll GetJobAsync every few seconds until Status is "done" or "failed".
    public Task<UploadAccepted> UploadDocumentAsync(
        string collection, Stream pdf, string fileName, bool replace = false, CancellationToken ct = default) =>
        SendAsync<UploadAccepted>(new HttpRequestMessage(HttpMethod.Post, "v1/documents")
        {
            Content = new MultipartFormDataContent
            {
                { new StringContent(collection), "collection" },
                { new StringContent(replace ? "true" : "false"), "replace" },
                { new StreamContent(pdf), "file", fileName },
            },
        }, ct);

    public Task<JobStatus> GetJobAsync(string jobId, CancellationToken ct = default) =>
        SendAsync<JobStatus>(new HttpRequestMessage(HttpMethod.Get, $"v1/documents/jobs/{Uri.EscapeDataString(jobId)}"), ct);

    // Removes a file (every version) from a collection. Needs documents:write.
    public async Task DeleteDocumentAsync(string collection, string fileName, CancellationToken ct = default)
    {
        var path = $"v1/documents?collection={Uri.EscapeDataString(collection)}&filename={Uri.EscapeDataString(fileName)}";
        using var response = await http.DeleteAsync(path, ct);
        await ThrowIfErrorAsync(response, ct);
    }

    private static HttpRequestMessage ForUser(
        HttpMethod method, string path, string userId, string? userRole = null, ChatRequest? body = null)
    {
        var message = new HttpRequestMessage(method, path);
        if (body is not null) message.Content = JsonContent.Create(body, options: Json);
        message.Headers.Add("ITI-User-Id", userId);
        if (userRole is not null) message.Headers.Add("ITI-User-Role", userRole);
        return message;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage message, CancellationToken ct)
    {
        using (message)
        {
            using var response = await http.SendAsync(message, ct);
            await ThrowIfErrorAsync(response, ct);
            return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
        }
    }

    private static ChatUsage UsageFrom(HttpResponseMessage response)
    {
        long? Header(string name) =>
            response.Headers.TryGetValues(name, out var values) && long.TryParse(values.FirstOrDefault(), out var n) ? n : null;
        return new ChatUsage(
            Header("ITI-User-Share-Used"), Header("ITI-User-Share-Limit"),
            Header("ITI-Tokens-Remaining"), Header("ITI-Tokens-Limit"));
    }

    // Every error reply is {"error": {"code", "message"}, "request_id"}.
    private static async Task ThrowIfErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var status = (int)response.StatusCode;
        JsonNode? body = null;
        try { body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)); }
        catch (JsonException) { }  // not our JSON (e.g. an HTML page from a proxy): code becomes http_<status>
        throw new ItiAiException(
            status,
            (string?)body?["error"]?["code"] ?? $"http_{status}",
            (string?)body?["error"]?["message"] ?? response.ReasonPhrase ?? "",
            (string?)body?["request_id"]);
    }
}