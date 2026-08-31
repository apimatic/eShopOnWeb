using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// Lines up the provider's own record of messages for a date range against
/// what eShop believes it sent.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int LocalNotificationCount { get; set; }
    public int MatchedCount { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new List<ReconciliationEntry>();
}

public class ReconciliationEntry
{
    public const string Matched = "matched";
    public const string MissingLocally = "missingLocally";
    public const string MissingAtProvider = "missingAtProvider";

    public string? MessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string? To { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
}
