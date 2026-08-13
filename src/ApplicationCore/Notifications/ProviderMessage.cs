using System;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// A provider-owned view of a single message, as returned by the messaging provider. This is the
/// state eShop does not own: the message identifier and its current delivery outcome.
/// </summary>
public class ProviderMessage
{
    /// <summary>The provider's message identifier (SID).</summary>
    public string? Sid { get; init; }

    /// <summary>The provider's current status/delivery outcome for the message (its own wire value).</summary>
    public string? Status { get; init; }

    /// <summary>The provider's numeric error code, if the message failed or was undelivered.</summary>
    public int? ErrorCode { get; init; }

    /// <summary>The sending number the provider recorded.</summary>
    public string? From { get; init; }

    /// <summary>The destination number the provider recorded. Treated as PII — never logged.</summary>
    public string? To { get; init; }

    /// <summary>When the provider recorded the message as sent (provider wire value, may be null).</summary>
    public string? DateSent { get; init; }
}
