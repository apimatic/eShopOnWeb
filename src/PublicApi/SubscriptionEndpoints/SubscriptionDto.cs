using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public int Id { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Lifecycle state reported by the billing system, for example <c>active</c>.</summary>
    public string? State { get; set; }

    /// <summary>False once the subscription has been cancelled, expired, or never activated.</summary>
    public bool IsLive { get; set; }

    public long? PriceInCents { get; set; }

    /// <summary>How the billing system collects payment, for example <c>remittance</c>.</summary>
    public string? PaymentCollectionMethod { get; set; }

    public decimal? Price { get; set; }

    public string? PriceDisplay { get; set; }

    public string? BillingPeriod { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>End of the current billing period — when the customer will next be billed.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }
}
