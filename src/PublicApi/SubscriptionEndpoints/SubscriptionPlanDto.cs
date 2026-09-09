using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan available for subscription, sourced live from Maxio Advanced Billing.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequireCreditCard { get; set; }
    public bool IsDefault { get; set; }
}

public class ListSubscriptionPlansResponse
{
    public ListSubscriptionPlansResponse()
    {
        Plans = new List<SubscriptionPlanDto>();
    }

    public ListSubscriptionPlansResponse(Guid correlationId) : this()
    {
        CorrelationId = correlationId;
    }

    public Guid CorrelationId { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public List<SubscriptionPlanDto> Plans { get; set; }
}
