using System;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// A provider-owned message record, as returned by the messaging API. This is the slice of the
/// provider's message resource the integration needs to act on and report on a message.
/// </summary>
public class SmsMessage
{
    public required string Sid { get; init; }
    public required string Status { get; init; }
    public string? To { get; init; }
    public string? From { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset? DateSent { get; init; }
}
