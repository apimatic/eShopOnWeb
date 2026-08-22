using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Last4 { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public string? Alias { get; set; }

    public static PaymentMethodDto From(PaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Last4 = method.Last4,
        Brand = method.Brand,
        Expiry = method.Expiry,
        Alias = method.Alias
    };
}
