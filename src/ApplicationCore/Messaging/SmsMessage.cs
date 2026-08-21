using System;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public class SmsMessage
{
    public string Sid { get; init; } = string.Empty;
    public string? Status { get; init; }
    public string? Body { get; init; }
    public string? To { get; init; }
    public string? From { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
    public int? ErrorCode { get; init; }
}
