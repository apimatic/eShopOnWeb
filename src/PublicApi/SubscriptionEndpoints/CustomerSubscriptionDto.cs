using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API projection of a shopper's subscription.</summary>
public class CustomerSubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }

    /// <summary>The next billing date (when the next scheduled charge is due).</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    /// <summary>When payment will next be attempted (usually equal to NextBillingDate).</summary>
    public DateTimeOffset? NextAssessmentDate { get; set; }

    public long CustomerId { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public DateTimeOffset? CreatedAt { get; set; }

    public static CustomerSubscriptionDto FromModel(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.Price,
        NextBillingDate = subscription.NextBillingDate,
        NextAssessmentDate = subscription.NextAssessmentDate,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference,
        CreatedAt = subscription.CreatedAt
    };
}
