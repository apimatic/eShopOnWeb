namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Models;

/// <summary>
/// A subscription plan (Maxio "product") offered to shoppers. Plans are projected from the
/// Maxio product family referenced by <c>Maxio:ProductFamilyHandle</c>.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Maxio numeric product id. Unstable across catalog re-seeds; use <see cref="Handle"/> as the stable key.</summary>
    public long ProductId { get; init; }

    /// <summary>Stable Maxio API handle, e.g. <c>eshop-pro</c>. This is what a shopper passes when subscribing.</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in integer cents.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price, expressed in whole currency units (dollars).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>The numerical billing interval (e.g. 1).</summary>
    public int BillingInterval { get; init; }

    /// <summary>Billing interval unit: <c>month</c> or <c>day</c>.</summary>
    public string BillingIntervalUnit { get; init; } = string.Empty;

    /// <summary>Length of the (optional) free trial, expressed as interval + unit; null when there is no trial.</summary>
    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }

    public bool Taxable { get; init; }

    /// <summary>Whether subscribing to this plan requires a stored payment method.</summary>
    public bool RequiresCreditCard { get; init; }

    /// <summary>Handle of the Maxio product family the plan belongs to.</summary>
    public string ProductFamilyHandle { get; init; } = string.Empty;
}
