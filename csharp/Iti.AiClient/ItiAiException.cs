namespace Iti.AiClient;

public sealed class ItiAiException(int status, string code, string message, string? requestId) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public string? RequestId { get; } = requestId;

    // A limit was reached (429): the key's month (token_cap_reached) or this person's day (user_daily_limit).
    // Show "limit reached"; never retry automatically.
    public bool IsLimit => Code is "token_cap_reached" or "user_daily_limit";

    // ITI paused this key (key_suspended), its trial is over (trial_ended), or this person is not in the trial's
    // pilot group (not_in_pilot). Show Message as it is; never retry automatically.
    public bool IsPaused => Code is "key_suspended" or "trial_ended" or "not_in_pilot";
}

