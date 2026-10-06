using System;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan purchasable through this app (a Maxio product in the
/// configured product family).
/// </summary>
public class SubscriptionPlanDto
{
    public long Id { get; set; }

    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Price in integer cents (money is carried in cents across the billing integration).</summary>
    public int PriceInCents { get; set; }

    /// <summary>Price in major currency units, for display.</summary>
    public decimal Price => PriceInCents / 100m;

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    /// <summary>Human-readable billing frequency, e.g. "Monthly".</summary>
    public string? DisplayInterval => Interval switch
    {
        null => null,
        1 when string.Equals(IntervalUnit, "month", StringComparison.OrdinalIgnoreCase) => "Monthly",
        1 when string.Equals(IntervalUnit, "day", StringComparison.OrdinalIgnoreCase) => "Daily",
        int n when string.Equals(IntervalUnit, "month", StringComparison.OrdinalIgnoreCase) => $"Every {n} months",
        int n => $"Every {n} days",
    };

    /// <summary>True when enrolling in this plan requires a payment method up front.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public bool Taxable { get; set; }

    public static SubscriptionPlanDto From(SubscriptionPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        Taxable = plan.Taxable,
    };
}
