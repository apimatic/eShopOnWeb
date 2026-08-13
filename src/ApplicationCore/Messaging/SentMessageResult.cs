namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// What the provider returned when a message was created (immediately or scheduled).
/// </summary>
/// <param name="ProviderSid">The provider's message identifier, or null if it was not returned.</param>
/// <param name="Status">The provider's initial status wire value (e.g. "queued", "accepted", "scheduled").</param>
/// <param name="ErrorCode">Provider error code, if any.</param>
/// <param name="ErrorMessage">Provider error message, if any. Never contains the destination number.</param>
public record SentMessageResult(string? ProviderSid, string Status, int? ErrorCode, string? ErrorMessage);
