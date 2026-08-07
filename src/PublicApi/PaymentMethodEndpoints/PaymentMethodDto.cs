using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>PCI-safe description of a saved card - enough for the shopper to recognise it, never full
/// card details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? Alias { get; set; }

    public static PaymentMethodDto From(PaymentMethod paymentMethod) => new()
    {
        PaymentMethodId = paymentMethod.Id,
        Brand = paymentMethod.Brand,
        Last4 = paymentMethod.Last4,
        Expiry = paymentMethod.Expiry,
        Alias = paymentMethod.Alias
    };
}
