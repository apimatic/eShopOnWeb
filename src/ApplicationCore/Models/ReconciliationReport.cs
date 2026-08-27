using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

public enum ReconciliationDisposition
{
    /// <summary>Present in both the provider's records and eShop's records.</summary>
    Matched = 0,

    /// <summary>The provider knows about the message but eShop has no record of it.</summary>
    MissingLocally = 1,

    /// <summary>eShop believes it sent the message but the provider has no record of it in range.</summary>
    MissingAtProvider = 2
}

public class ReconciliationEntry
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public ReconciliationDisposition Disposition { get; set; }
    public string? ProviderStatus { get; set; }
    public int? ProviderErrorCode { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string? FromNumber { get; set; }
    public int ProviderMessageCount { get; set; }
    public int LocalNotificationCount { get; set; }
    public int MatchedCount { get; set; }
    public int MissingLocallyCount { get; set; }
    public int MissingAtProviderCount { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new List<ReconciliationEntry>();
}
