using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEntryDto
{
    public string Sid { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public int? OrderId { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ReconciliationResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>The sending number the provider was queried for.</summary>
    public string FromNumber { get; set; } = string.Empty;

    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }

    /// <summary>Messages both the provider and eShop know about.</summary>
    public List<ReconciliationEntryDto> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about but eShop does not.</summary>
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop recorded sending but the provider did not return.</summary>
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();
}
