using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// Lines up the provider's record of messages against eShop's own records.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>Messages present in both the provider's record and eShop's.</summary>
    public List<ReconciledMessage> Matched { get; set; } = new List<ReconciledMessage>();

    /// <summary>Messages the provider knows about from our sending number that eShop has no record of.</summary>
    public List<ReconciledMessage> ProviderOnly { get; set; } = new List<ReconciledMessage>();

    /// <summary>Messages eShop believes it sent that the provider has no record of in the range.</summary>
    public List<ReconciledMessage> EshopOnly { get; set; } = new List<ReconciledMessage>();
}

public class ReconciledMessage
{
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? LocalStatus { get; set; }
}
