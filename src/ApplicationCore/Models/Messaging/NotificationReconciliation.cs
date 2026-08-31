using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

/// <summary>One line of the reconciliation report.</summary>
public class ReconciliationEntry
{
    public string? ProviderMessageId { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public string? DateSent { get; set; }

    /// <summary>The message text as currently held by the provider (empty once disposed of).</summary>
    public string? ProviderBody { get; set; }
}

/// <summary>
/// The provider's record of messages for a range lined up against what eShop believes it sent.
/// </summary>
public class NotificationReconciliation
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>True when the provider had more pages than the report walked.</summary>
    public bool Truncated { get; set; }

    public List<ReconciliationEntry> Matched { get; set; } = new();
    public List<ReconciliationEntry> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntry> LocalOnly { get; set; } = new();
}
