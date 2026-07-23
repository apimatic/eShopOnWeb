namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to, as exposed by the billing provider.
/// <see cref="Handle"/> is the durable identifier; provider-assigned numeric ids are not stable
/// across a sandbox re-seed and are therefore never persisted by eShopOnWeb.
/// </summary>
public sealed record SubscriptionPlan
{
    /// <summary>Stable, human-readable identifier (for example <c>eshop-pro</c>).</summary>
    public required string Handle { get; init; }

    /// <summary>Provider-assigned numeric id. Informational only — never persist it.</summary>
    public int? ProviderId { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price in minor units (cents). The provider models money in cents.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period (for example 1).</summary>
    public int Interval { get; init; }

    /// <summary>The billing period unit reported by the provider (for example <c>month</c>).</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>True when the provider refuses to enrol without a stored payment method.</summary>
    public bool RequiresPaymentMethod { get; init; }

    /// <summary>Handle of the product family this plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }

    /// <summary>Recurring price in major units (dollars).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>A display string such as <c>$299.00 / month</c>.</summary>
    public string BillingDescription => Interval == 1
        ? $"$ {Price:N2} / {IntervalUnit}"
        : $"$ {Price:N2} every {Interval} {IntervalUnit}s";
}
