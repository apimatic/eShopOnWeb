using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan offered by the billing system.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class ListSubscriptionPlansRequest : BaseRequest
{
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();

    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse() { }
}

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to. When omitted, the first (lowest-priced)
    /// plan of the configured product family is used.
    /// </summary>
    public string? ProductHandle { get; set; }
}

public class SubscriptionSummaryDto
{
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime? NextBillingDateUtc { get; set; }
    public bool Created { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public SubscriptionSummaryDto? Subscription { get; set; }

    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse() { }
}

public class ListMySubscriptionsRequest : BaseRequest
{
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionSummaryDto> Subscriptions { get; set; } = new();

    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse() { }
}