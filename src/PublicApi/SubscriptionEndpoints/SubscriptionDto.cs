using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan offered to shoppers, sourced from Maxio Advanced Billing.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public decimal Price { get; set; }
    public int BillingInterval { get; set; }
    public string BillingIntervalUnit { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }
}

/// <summary>
/// A subscription held by the current user, sourced from Maxio Advanced Billing.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public decimal Price { get; set; }
    public System.DateTimeOffset? CreatedAt { get; set; }
    public System.DateTimeOffset? ActivatedAt { get; set; }
    public System.DateTimeOffset? CurrentPeriodStart { get; set; }
    public System.DateTimeOffset? NextBillingAt { get; set; }
    public System.DateTimeOffset? CanceledAt { get; set; }

    public static SubscriptionDto From(SubscriptionDetails details) =>
        new SubscriptionDto
        {
            Id = details.Id,
            State = details.State,
            PlanHandle = details.PlanHandle,
            PlanName = details.PlanName,
            PriceInCents = details.PriceInCents,
            Price = details.PriceInCents / 100m,
            CreatedAt = details.CreatedAt,
            ActivatedAt = details.ActivatedAt,
            CurrentPeriodStart = details.CurrentPeriodStart,
            NextBillingAt = details.NextBillingAt,
            CanceledAt = details.CanceledAt
        };
}
