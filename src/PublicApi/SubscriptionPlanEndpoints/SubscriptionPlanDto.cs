using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

/// <summary>A subscribable plan surfaced to the eShopOnWeb storefront (a Maxio product).</summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable API handle used to subscribe (Maxio numeric ids are not stable across re-seeds).</summary>
    public string Handle { get; set; } = string.Empty;
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }

    /// <summary>Recurring price in integer cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price in major currency units (cents / 100), for convenient display.</summary>
    public decimal Price { get; set; }

    /// <summary>Billing interval count, e.g. 1.</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit, e.g. "month" or "day".</summary>
    public string? IntervalUnit { get; set; }

    public string? ProductFamilyHandle { get; set; }

    /// <summary>Whether Maxio requires a payment method to subscribe to this plan.</summary>
    public bool RequireCreditCard { get; set; }

    public static SubscriptionPlanDto FromMaxio(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Id = product.Id,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Price = product.PriceInCents / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        ProductFamilyHandle = product.ProductFamily?.Handle,
        RequireCreditCard = product.RequireCreditCard
    };
}
