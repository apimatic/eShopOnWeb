using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Notifications;

/// <summary>
/// A reconciliation of the provider's own record of messages (for this application's configured
/// sending number) against what eShop believes it sent, over a date range. Discrepancies in either
/// direction are made visible.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    /// <summary>Messages present in both the provider's record and eShop's.</summary>
    public List<ReconciledMessage> Matched { get; init; } = new();

    /// <summary>Messages the provider knows about that eShop has no record of.</summary>
    public List<ReconciledMessage> ProviderOnly { get; init; } = new();

    /// <summary>Messages eShop believes it sent that the provider's record does not contain.</summary>
    public List<ReconciledMessage> EShopOnly { get; init; } = new();

    public int ProviderMessageCount { get; init; }
    public int EShopMessageCount { get; init; }
}

/// <summary>One line of a reconciliation report. The destination is masked; the message body is never included.</summary>
public class ReconciledMessage
{
    /// <summary>Provider message id (the join key between the two records).</summary>
    public string? ProviderMessageId { get; init; }

    /// <summary>eShop's notification id, when eShop has a record of this message.</summary>
    public int? NotificationId { get; init; }

    /// <summary>Provider-reported status, when the provider has a record of this message.</summary>
    public string? ProviderStatus { get; init; }

    /// <summary>eShop's last-known status, when eShop has a record of this message.</summary>
    public string? EShopStatus { get; init; }

    public int? OrderId { get; init; }
    public DateTimeOffset? DateSent { get; init; }

    /// <summary>Masked destination (never the full number).</summary>
    public string? To { get; init; }
}
