using System;
using Microsoft.eShopWeb.PublicApi.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated user to a subscription plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The API handle of the plan (Maxio product) to subscribe to, e.g. "eshop-pro".
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}

/// <summary>
/// Response confirming a subscription.
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public CreateSubscriptionResponse() { }

    public SubscriptionDetailsDto Subscription { get; set; } = new();
}

/// <summary>
/// A user's subscription as recorded in Maxio Advanced Billing.
/// </summary>
public class SubscriptionDetailsDto
{
    public int MaxioSubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string PriceFormatted { get; set; } = string.Empty;
    public string NextBillingDate { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;
    public string PaymentCollectionMethod { get; set; } = string.Empty;
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}