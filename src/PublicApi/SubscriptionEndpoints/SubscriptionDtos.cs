using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated user to a plan.
/// </summary>
public class CreateSubscriptionRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

/// <summary>
/// The state of a user's subscription as billed by the billing system.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string Price { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime? NextBillingAt { get; set; }
    public long BillingSubscriptionId { get; set; }
}

public class CreateSubscriptionResponse
{
    public string CorrelationId { get; set; } = string.Empty;
    public SubscriptionDto Subscription { get; set; } = new();
}

public class ListMySubscriptionsResponse
{
    public string CorrelationId { get; set; } = string.Empty;
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
