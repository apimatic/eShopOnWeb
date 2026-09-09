using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Response for the subscription plans list.</summary>
public class GetSubscriptionPlansResponse
{
    public GetSubscriptionPlansResponse() { }

    public GetSubscriptionPlansResponse(System.Guid correlationId)
    {
        CorrelationId = correlationId;
    }

    public System.Guid CorrelationId { get; set; }
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

/// <summary>A subscribable plan as exposed to API clients.</summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Recurring price, minor units (cents).</summary>
    public int PriceInCents { get; set; }
    /// <summary>Billing interval length.</summary>
    public int Interval { get; set; }
    /// <summary>Billing interval unit, e.g. "month".</summary>
    public string IntervalUnit { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
}
