using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class SmsMessageSnapshot
{
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Body { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? DateCreated { get; init; }
    public string? DateSent { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? SentAt { get; init; }
}
