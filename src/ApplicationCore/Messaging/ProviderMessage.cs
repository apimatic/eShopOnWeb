using System;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public sealed class ProviderMessage
{
    public required string Sid { get; init; }
    public required string Status { get; init; }
    public string? Body { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
    public DateTimeOffset? ScheduledFor { get; init; }
}
