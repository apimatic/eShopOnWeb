using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan offered by the configured Maxio product family.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier used when subscribing. Numeric Maxio ids are not stable; handles are.</summary>
    public string Handle { get; set; } = string.Empty;

    public string? Name { get; set; }
    public string? Description { get; set; }

    /// <summary>Recurring price in major units, e.g. 299.00.</summary>
    public decimal? Price { get; set; }

    /// <summary>Recurring price in minor units, as Maxio stores it.</summary>
    public long? PriceInCents { get; set; }

    public string? Currency { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int? Interval { get; set; }

    /// <summary>Billing interval unit, e.g. <c>month</c>.</summary>
    public string? IntervalUnit { get; set; }

    public bool HasTrial { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }

    /// <summary>One-off setup fee in major units, when the plan has one.</summary>
    public decimal? SetupFee { get; set; }

    /// <summary>True when Maxio requires a card before a subscription can be created.</summary>
    public bool PaymentMethodRequired { get; set; }

    /// <summary>True when Maxio asks for a card but does not enforce one.</summary>
    public bool PaymentMethodRequested { get; set; }

    /// <summary>True for the plan used when a subscribe request does not name one.</summary>
    public bool IsDefault { get; set; }
}
