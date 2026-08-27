using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int ProviderMessageCount { get; set; }
    public int LocalNotificationCount { get; set; }
    public int MatchedCount { get; set; }

    /// <summary>Messages the provider knows about, lined up with the local notification when one exists.</summary>
    public List<ReconciliationEntryDto> Entries { get; set; } = new();

    /// <summary>Messages the provider knows about that eShop has no record of.</summary>
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();

    /// <summary>Notifications eShop believes it sent that the provider has no record of in range.</summary>
    public List<EShopOnlyEntryDto> EShopOnly { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string MessageSid { get; set; } = string.Empty;
    public string? To { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? DateSent { get; set; }
    public int? NotificationId { get; set; }
}

public class EShopOnlyEntryDto
{
    public int NotificationId { get; set; }
    public string? MessageSid { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedOn { get; set; }
}
