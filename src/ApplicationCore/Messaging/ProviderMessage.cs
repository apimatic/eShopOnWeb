using System;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// One message as the provider records it, used when reconciling the provider's own list against
/// what eShop believes it sent.
/// </summary>
public class ProviderMessage
{
    public string Sid { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string? From { get; init; }

    public string? To { get; init; }

    public DateTimeOffset? DateSent { get; init; }

    public int? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }
}
