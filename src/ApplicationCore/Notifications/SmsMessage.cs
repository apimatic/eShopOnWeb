using System;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// A provider-neutral view of a single message as the messaging provider knows it. Populated from the
/// provider's message resource (create / fetch / list responses).
/// </summary>
public class SmsMessage
{
    public string Sid { get; init; } = default!;
    public string? Status { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Body { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
}
