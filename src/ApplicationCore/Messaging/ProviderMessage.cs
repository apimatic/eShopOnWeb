using System;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>The provider's own record of a single message.</summary>
public record ProviderMessage
{
    public required string Sid { get; init; }
    public string? To { get; init; }
    public string? From { get; init; }
    public string? Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public string? Body { get; init; }
}
