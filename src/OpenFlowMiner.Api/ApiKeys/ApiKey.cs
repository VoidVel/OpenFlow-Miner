namespace OpenFlowMiner.Api.ApiKeys;

/// <summary>
/// An opaque API key issued for <b>rate-limit tiering only</b>. It is deliberately NOT an account:
/// there is no owner, email, password, or profile associated with it — just a random token, when it
/// was created, and when it expires. Keys cannot be listed, looked up by anyone else, or managed.
/// </summary>
public sealed record ApiKey(string Key, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);
