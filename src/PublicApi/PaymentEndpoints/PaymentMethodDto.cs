using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>A saved card, described with safe details only (never full card data).</summary>
public class PaymentMethodDto
{
    public int Id { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;

    public static PaymentMethodDto From(SavedPaymentMethod method) => new()
    {
        Id = method.Id,
        Brand = method.CardBrand,
        Last4 = method.Last4,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName
    };
}
