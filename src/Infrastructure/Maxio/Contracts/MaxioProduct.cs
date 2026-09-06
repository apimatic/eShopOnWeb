using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>
/// Maxio product - what eShopOnWeb offers as a subscription plan.
/// Only the fields the integration actually uses are modelled; Maxio may add more at any time.
/// </summary>
public class MaxioProduct
{
    public long Id { get; set; }

    public string? Name { get; set; }

    /// <summary>The stable API handle. Preferred over <see cref="Id"/>, which is not stable across seeds.</summary>
    public string? Handle { get; set; }

    public string? Description { get; set; }

    public long PriceInCents { get; set; }

    public int Interval { get; set; }

    /// <summary>"month" or "day".</summary>
    public string? IntervalUnit { get; set; }

    public int? TrialInterval { get; set; }

    public long? TrialPriceInCents { get; set; }

    /// <summary>Non-null once the product is archived; archived products are not offered.</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>Whether a payment profile must exist before a subscription to this product can be created.</summary>
    public bool RequireCreditCard { get; set; }

    public bool Taxable { get; set; }

    public string? ProductPricePointHandle { get; set; }

    public MaxioProductFamily? ProductFamily { get; set; }
}
