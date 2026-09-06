namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// Plans live in the billing system of record (Maxio Advanced Billing); eShopOnWeb never
/// stores or invents plan definitions, it only projects them for display.
/// </summary>
public sealed record SubscriptionPlan
{
    /// <summary>Stable API handle of the plan, e.g. <c>eshop-pro</c>. Numeric ids are deliberately not exposed.</summary>
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest unit of <see cref="Currency"/>.</summary>
    public required long PriceInCents { get; init; }

    /// <summary>ISO 4217 code, taken from the billing site configuration.</summary>
    public required string Currency { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period, e.g. <c>1</c> for monthly.</summary>
    public required int Interval { get; init; }

    /// <summary><c>month</c> or <c>day</c>.</summary>
    public required string IntervalUnit { get; init; }

    /// <summary>True when the billing system refuses signup unless a payment profile is supplied.</summary>
    public required bool PaymentMethodRequired { get; init; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public required string ProductFamilyHandle { get; init; }

    public decimal Price => decimal.Divide(PriceInCents, 100m);
}
