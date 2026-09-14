using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }

    public static SubscriptionPlanDto FromMaxioProduct(MaxioProduct product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        Price = product.PriceInCents / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard
    };
}
