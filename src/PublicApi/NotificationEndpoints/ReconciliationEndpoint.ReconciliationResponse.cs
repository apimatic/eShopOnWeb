using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int LocalNotificationCount { get; set; }
    public int MatchedCount { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new();
    public string? Message { get; set; }
}

public class ReconciliationEntry
{
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public int? NotificationId { get; set; }
    public string? LocalStatus { get; set; }

    /// <summary>matched | missingLocally (provider knows it, eShop doesn't) | missingAtProvider (the reverse).</summary>
    public string Match { get; set; } = string.Empty;
}
