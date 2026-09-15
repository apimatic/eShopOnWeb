using System;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// The outcome of asking the provider to create (or schedule) a message. A returned result means
/// the provider accepted the request (an acceptance receipt, not a delivery receipt): it carries
/// the provider's message identifier and the initial status. Delivery is observed later.
/// </summary>
public class SmsSendResult
{
    public required string MessageSid { get; init; }
    public required string Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset? ScheduledSendAt { get; init; }
}
