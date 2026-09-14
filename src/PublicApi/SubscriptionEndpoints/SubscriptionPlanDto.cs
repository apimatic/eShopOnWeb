using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan (Maxio product) a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    public long ProductId { get; set; }

    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in the minor unit (cents) of the site currency.</summary>
    public long? PriceInCents { get; set; }

    public int? Interval { get; set; }

    /// <summary>'month' or 'day' (see Interval-Unit.yaml in the Maxio spec).</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>True when subscribing to this plan requires a stored payment method.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public string? ProductFamilyHandle { get; set; }

    public string? ProductFamilyName { get; set; }

    public static SubscriptionPlanDto FromMaxioProduct(MaxioProduct product)
    {
        return new SubscriptionPlanDto
        {
            ProductId = product.Id,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? product.Handle ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit,
            RequiresPaymentMethod = product.RequireCreditCard ?? false,
            ProductFamilyHandle = product.ProductFamily?.Handle,
            ProductFamilyName = product.ProductFamily?.Name
        };
    }
}
