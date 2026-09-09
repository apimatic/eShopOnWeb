using System;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to. When omitted, the first active
    /// plan of the configured product family is used.
    /// </summary>
    public string? PlanHandle { get; set; }
}

public class SubscriptionDto
{
    /// <summary>
    /// The Maxio subscription id (Maxio is the billing system of record).
    /// </summary>
    public int SubscriptionId { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public long PriceInCents { get; set; }

    /// <summary>
    /// Formatted recurring price, e.g. "$299.00".
    /// </summary>
    public string Price { get; set; } = string.Empty;

    /// <summary>
    /// Maxio subscription state (e.g. "active", "trialing", "past_due").
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// When the next regularly scheduled charge will occur.
    /// </summary>
    public DateTime? NextBillingAt { get; set; }

    public bool AlreadySubscribed { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public SubscriptionDto? Subscription { get; set; }

    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public CreateSubscriptionResponse() { }
}
