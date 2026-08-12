using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset FromUtc { get; set; }
    public DateTimeOffset ToUtc { get; set; }

    /// <summary>The application's sending number the provider was queried for.</summary>
    public string FromNumber { get; set; } = string.Empty;

    public int ProviderCount { get; set; }
    public int EShopCount { get; set; }
    public int MatchedCount { get; set; }

    /// <summary>Messages both sides agree on, matched by provider SID.</summary>
    public List<ReconciliationMatchDto> Matched { get; set; } = new();

    /// <summary>Messages the provider has that eShop has no record of.</summary>
    public List<ProviderOnlyDto> OnlyAtProvider { get; set; } = new();

    /// <summary>Notifications eShop recorded as sent that the provider did not return for this range.</summary>
    public List<EShopOnlyDto> OnlyInEShop { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public string Sid { get; set; } = string.Empty;
    public int NotificationId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public bool StatusMatches { get; set; }
}

public class ProviderOnlyDto
{
    public string Sid { get; set; } = string.Empty;
    public string? Status { get; set; }

    /// <summary>Destination, masked — the shopper's full number is not exposed.</summary>
    public string? To { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public int? ErrorCode { get; set; }
}

public class EShopOnlyDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string? Sid { get; set; }
    public string? Status { get; set; }
}
