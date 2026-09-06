namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, projected from the billing system's catalog.
/// </summary>
public sealed record SubscriptionPlan
{
    /// <summary>Stable, human readable identifier of the plan. This is what callers pass to subscribe.</summary>
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price per billing period, in <see cref="Currency"/>.</summary>
    public required decimal Price { get; init; }

    /// <summary>ISO 4217 currency code the plan is billed in, as reported by the billing site.</summary>
    public string? Currency { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period (e.g. 1 with "month").</summary>
    public int? IntervalLength { get; init; }

    /// <summary>Unit of the billing period, e.g. "month" or "day".</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>True when the billing system refuses to start a subscription without a payment method on file.</summary>
    public required bool RequiresPaymentMethod { get; init; }
}
