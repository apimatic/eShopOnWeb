using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Alias { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;

    public static PaymentMethodDto From(PaymentMethod paymentMethod) => new()
    {
        PaymentMethodId = paymentMethod.Id,
        Alias = paymentMethod.Alias ?? string.Empty,
        Brand = paymentMethod.Brand ?? string.Empty,
        Last4 = paymentMethod.Last4 ?? string.Empty,
        Expiry = paymentMethod.Expiry ?? string.Empty
    };
}
