using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

/// <summary>
/// The provider's own record of a message, as returned when listing messages for reconciliation.
/// </summary>
public record ProviderMessageRecord
{
    public required string Sid { get; init; }

    public string? To { get; init; }

    public string? From { get; init; }

    public string? Status { get; init; }

    public int? ErrorCode { get; init; }

    public DateTimeOffset? DateSent { get; init; }
}
