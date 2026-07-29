namespace Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;

/// <summary>
/// A plan a shopper can subscribe to. Projected from a Maxio "product" that
/// belongs to the configured product family.
/// </summary>
public record SubscriptionPlan
{
    /// <summary>The stable Maxio product handle (e.g. <c>eshop-pro</c>). Used to subscribe.</summary>
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public int PriceInCents { get; init; }

    /// <summary>Numeric length of a billing period, paired with <see cref="IntervalUnit"/>.</summary>
    public int Interval { get; init; }

    /// <summary>Billing period unit, either <c>month</c> or <c>day</c>.</summary>
    public required string IntervalUnit { get; init; }

    /// <summary>Whether Maxio requires a payment method to be captured before subscribing.</summary>
    public bool RequiresPaymentMethod { get; init; }

    /// <summary>Maxio numeric product id. Not stable across catalog re-seeds; prefer <see cref="Handle"/>.</summary>
    public int ProductId { get; init; }
}
