using Microsoft.eShopWeb.PublicApi.MaxioBilling;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public long Id { get; set; }

    public string? Handle { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public long? PriceInCents { get; set; }

    public decimal Price { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public string? ProductFamilyHandle { get; set; }

    public static SubscriptionPlanDto FromMaxio(MaxioProduct product)
    {
        return new SubscriptionPlanDto
        {
            Id = product.Id,
            Handle = product.Handle,
            Name = product.Name,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Price = product.PriceInCents is null ? 0 : product.PriceInCents.Value / 100m,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit,
            ProductFamilyHandle = product.ProductFamily?.Handle
        };
    }
}
