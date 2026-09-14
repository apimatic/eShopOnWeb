using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>A subscription plan offered by the storefront (a Maxio product).</summary>
public class SubscriptionPlanDto
{
    /// <summary>Maxio product id.</summary>
    public int Id { get; set; }

    /// <summary>Stable API handle used to subscribe (e.g. "eshop-pro").</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in the smallest currency unit.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount (cents / 100).</summary>
    public decimal Price { get; set; }

    /// <summary>ISO 4217 currency code when it can be determined (e.g. "USD").</summary>
    public string? CurrencyCode { get; set; }

    /// <summary>Billing interval magnitude (e.g. 1 for "every 1 month").</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit: "month" or "day".</summary>
    public string IntervalUnit { get; set; } = string.Empty;
}

/// <summary>A subscription owned by the caller (a Maxio subscription).</summary>
public class SubscriptionDto
{
    /// <summary>Maxio subscription id.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>Current Maxio subscription state (e.g. "active").</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Handle of the subscribed plan.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>The recurring amount this subscription is billed, in the smallest currency unit.</summary>
    public long PriceInCents { get; set; }

    public decimal Price { get; set; }

    public string? CurrencyCode { get; set; }

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Next billing/assessment date.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}
