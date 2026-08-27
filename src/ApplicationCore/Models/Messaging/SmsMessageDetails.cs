using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

/// <summary>The provider's own record of a single message.</summary>
public class SmsMessageDetails
{
    public required string MessageSid { get; init; }
    public string? Status { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? ErrorCode { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
    public DateTimeOffset? DateSent { get; init; }
}
