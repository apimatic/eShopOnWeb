using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A reconciliation of the provider's own record of sent messages against eShop's records,
/// over a date range, restricted to this application's configured sending number.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>The sending number the provider was asked about.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Total messages the provider reported for the number over the range.</summary>
    public int ProviderCount { get; set; }

    /// <summary>Total eShop notifications with a provider identifier over the range.</summary>
    public int EShopCount { get; set; }

    /// <summary>Messages both the provider and eShop know about, keyed by message SID.</summary>
    public List<ReconciliationEntry> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about that eShop has no record of.</summary>
    public List<ReconciliationEntry> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider did not return.</summary>
    public List<ReconciliationEntry> EShopOnly { get; set; } = new();
}

public class ReconciliationEntry
{
    public string? MessageSid { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
}
