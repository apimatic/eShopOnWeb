namespace Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

/// <summary>Outcome of asking the provider to send (or schedule) a message.</summary>
public class SmsSendResult
{
    public bool Accepted { get; init; }
    public string? MessageSid { get; init; }

    /// <summary>Provider status at creation time, e.g. "queued" or "scheduled".</summary>
    public string? Status { get; init; }

    /// <summary>Canonical sender identity the provider used for the message.</summary>
    public string? From { get; init; }

    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
