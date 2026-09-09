using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscribable plan offered by Maxio Advanced Billing.</summary>
public sealed class SubscriptionPlanDto
{
    public int MaxioProductId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public sealed class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
