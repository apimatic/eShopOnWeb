using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconcileNotificationsResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>True when a page cap stopped the provider listing before its end.</summary>
    public bool Truncated { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int LocalOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

public class ReconciliationEntryDto
{
    /// <summary>The provider's message identifier.</summary>
    public string MessageSid { get; set; } = string.Empty;

    /// <summary>matched | providerOnly | localOnly</summary>
    public string Reconciliation { get; set; } = string.Empty;
    public int? NotificationId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public bool? StatusMatch { get; set; }
    public string? To { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}
