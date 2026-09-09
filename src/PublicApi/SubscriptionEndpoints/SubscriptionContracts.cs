using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. The request body is optional;
/// when no planHandle is supplied, the first plan in the configured product
/// family (lowest price) is used.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}

/// <summary>Response for creating (or reusing) a subscription.</summary>
public class CreateSubscriptionResponse
{
    public CreateSubscriptionResponse() { }

    public CreateSubscriptionResponse(Guid correlationId)
    {
        CorrelationId = correlationId;
    }

    public Guid CorrelationId { get; set; }

    /// <summary>Maxio subscription id (the billing system of record).</summary>
    public int SubscriptionId { get; set; }

    /// <summary>True when a live subscription for this user+plan already existed and was returned.</summary>
    public bool AlreadySubscribed { get; set; }

    /// <summary>True when a new Maxio customer record was created for this user during this call.</summary>
    public bool CreatedCustomer { get; set; }

    public SubscriptionDto? Subscription { get; set; }
}

/// <summary>A subscription as exposed to API clients.</summary>
public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    /// <summary>Subscription state, e.g. "active".</summary>
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    /// <summary>Recurring price, minor units (cents).</summary>
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    /// <summary>Next date the subscriber will be billed (UTC).</summary>
    public DateTime? NextBillingDateUtc { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
}

/// <summary>Response listing the authenticated user's subscriptions.</summary>
public class ListMySubscriptionsResponse
{
    public ListMySubscriptionsResponse() { }

    public ListMySubscriptionsResponse(Guid correlationId)
    {
        CorrelationId = correlationId;
    }

    public Guid CorrelationId { get; set; }
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
