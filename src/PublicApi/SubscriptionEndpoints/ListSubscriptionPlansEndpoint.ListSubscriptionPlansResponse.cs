using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response for listing the available subscription plans.
/// </summary>
public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }

    public ListSubscriptionPlansResponse() { }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

/// <summary>
/// A subscription plan (Maxio product) offered to shoppers.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string PriceFormatted { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
}