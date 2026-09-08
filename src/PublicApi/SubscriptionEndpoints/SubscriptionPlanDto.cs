using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan surfaced by <c>GET /api/subscription-plans</c>. The plan catalog lives
/// in Maxio Advanced Billing; the API handle is the stable identifier used to subscribe.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Maxio product id. Not stable across catalog re-seeds; prefer <see cref="Handle"/>.</summary>
    public long ProductId { get; set; }

    /// <summary>Stable Maxio product API handle, used to subscribe.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The recurring price in cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>The recurring price, in dollars.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Billing interval length (e.g. 1 for "every month").</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit: <c>month</c> or <c>day</c>.</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Whether Maxio will require a card on file to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public bool Taxable { get; set; }

    internal static SubscriptionPlanDto FromMaxio(MaxioProduct product) => new SubscriptionPlanDto
    {
        ProductId = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard,
        Taxable = product.Taxable
    };
}
