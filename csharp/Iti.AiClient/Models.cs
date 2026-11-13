namespace Iti.AiClient;

public sealed record ChatRequest(
    string Question, 
    string Collection, 
    string? ConversationId = null);

public sealed record ChatResponse(
    string Answer,
    bool Found,
    List<Citation> Citations,
    string ConversationId,
    string RequestId)
{
    // Read from the reply's headers (v1.1), not its JSON. Null fields: no limit of that kind.
    public ChatUsage? Usage { get; init; }
}

// The limits as they were BEFORE this question (keys with a plan; others send only ShareUsed).
// ShareUsed / ShareLimit: this person's model tokens today through this key (resets at midnight, Manila).
// TokensRemaining / TokensLimit: this key's month (top-ups included; resets on the 1st, Manila).
public sealed record ChatUsage(long? ShareUsed, long? ShareLimit, long? TokensRemaining, long? TokensLimit)
{
    // 80 % or more used: a good moment to warn the person (or your admins, for the key's month).
    public bool NearDailyShare => ShareLimit > 0 && ShareUsed >= ShareLimit * 0.8;
    public bool NearMonthLimit => TokensLimit > 0 && TokensRemaining <= TokensLimit * 0.2;
}

// GET /v1/usage/me: Day = today in Manila ("2026-11-04"); Limit null = no daily limit.
public sealed record UsageMe(string Day, long Used, long? Limit);

// GET /v1/usage/key (v1.2): this key's month. Month = "2026-11" (Manila). Limit / Remaining null = no plan, no limit.
// Used = billable tokens only (answered questions). At the limit Remaining is 0; only questions are refused.
public sealed record UsageKey(string Month, long Used, long? Limit, long? Remaining)
{
    // 80 % or more used: tell your admins.
    public bool NearMonthLimit => Limit > 0 && Remaining <= Limit * 0.2;
}

public sealed record Citation(
    string DocId, 
    string Title, 
    int? Page, 
    string Snippet,
    string? Url = null);

public sealed record ChatStreamEvent(
    string? ConversationId,
    string? RequestId,
    string? Text,
    string? Answer,
    bool Found,
    List<Citation>? Citations,
    string? Stage = null,   // v1.4: on Kind "status" only: thinking, part, records, documents or web
    string? Status = null)  // v1.4: on Kind "status" only: what to show instead of a blinking cursor
{
    public string Kind { get; init; } = "";
    // Set on the first event ("meta") only, from the stream's headers (v1.1).
    public ChatUsage? Usage { get; init; }
}

public sealed record ConversationSummary(
    string ConversationId,
    string Collection,
    string FirstQuestion, 
    DateTimeOffset CreatedAt);
    
public sealed record MessageOut(
    string Role, 
    string Content, 
    string RequestId, 
    DateTimeOffset CreatedAt,
    List<Citation>? Citations
    );
    
public sealed record Suggestion(
    string Question,
    string File);

public sealed record UploadAccepted(
    string JobId, 
    string DocumentId,
    string Status);
    
public sealed record JobStatus (
    string JobId, 
    string DocumentId, 
    string Filename, 
    string Status,
    int? Chunks,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt,
    string? Warning = null);   // indexed, but some numbers look unreadable (bad OCR): answers from those pages may be refused