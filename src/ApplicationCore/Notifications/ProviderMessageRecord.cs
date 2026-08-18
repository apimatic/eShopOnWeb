using System;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// The provider's own record of a message, as returned by its list API for reconciliation.
/// Deliberately excludes the destination number (personal data).
/// </summary>
public class ProviderMessageRecord
{
    public string? Sid { get; init; }

    public string? Status { get; init; }

    /// <summary>The sending number the message went out from.</summary>
    public string? From { get; init; }

    public DateTimeOffset? DateSent { get; init; }

    public int? ErrorCode { get; init; }
}
