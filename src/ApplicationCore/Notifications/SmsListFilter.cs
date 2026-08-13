using System;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// A filter for asking the provider for the messages it knows about, used by reconciliation.
/// </summary>
public class SmsListFilter
{
    /// <summary>Restrict to messages sent from this number (the application's own configured sender).</summary>
    public string? From { get; init; }

    /// <summary>Inclusive lower bound on the provider's sent-date.</summary>
    public DateTimeOffset? DateSentAfter { get; init; }

    /// <summary>Inclusive upper bound on the provider's sent-date.</summary>
    public DateTimeOffset? DateSentBefore { get; init; }
}
