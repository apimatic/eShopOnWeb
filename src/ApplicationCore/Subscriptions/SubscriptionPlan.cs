namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan, sourced from a Maxio (Advanced Billing) product within the
/// configured product family. Prices are expressed in cents to avoid rounding, matching
/// the Maxio contract (<c>price_in_cents</c>).
/// </summary>
public sealed class SubscriptionPlan
{
    public SubscriptionPlan(
        int productId,
        string handle,
        string name,
        string? description,
        int priceInCents,
        int interval,
        string intervalUnit,
        string productFamilyHandle,
        bool requiresPaymentMethod)
    {
        ProductId = productId;
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        ProductFamilyHandle = productFamilyHandle;
        RequiresPaymentMethod = requiresPaymentMethod;
    }

    /// <summary>Maxio numeric product id (not stable across re-seeds; prefer <see cref="Handle"/>).</summary>
    public int ProductId { get; }

    /// <summary>Stable API handle used to subscribe to this plan.</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    public int PriceInCents { get; }

    /// <summary>Billing interval count (e.g. 1).</summary>
    public int Interval { get; }

    /// <summary>Billing interval unit (e.g. "month").</summary>
    public string IntervalUnit { get; }

    public string ProductFamilyHandle { get; }

    /// <summary>Whether Maxio requires a payment method on file to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; }
}
