using Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan (Maxio product) exposed to API clients.
/// </summary>
public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool Taxable { get; set; }
    public bool RequireCreditCard { get; set; }

    public static SubscriptionPlanDto FromProduct(Product product)
    {
        return new SubscriptionPlanDto
        {
            Id = product.Id,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Price = product.PriceInCents / 100m,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? string.Empty,
            Taxable = product.Taxable,
            RequireCreditCard = product.RequireCreditCard
        };
    }
}
