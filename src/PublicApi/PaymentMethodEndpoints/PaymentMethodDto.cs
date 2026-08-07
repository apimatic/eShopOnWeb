using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Safe, display-only view of a saved card. Deliberately never includes the card number, CVC or the
/// PayPal vault token — only enough for a shopper to recognise which card it is.
/// </summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? Alias { get; set; }

    public static PaymentMethodDto FromEntity(PaymentMethod paymentMethod) => new()
    {
        PaymentMethodId = paymentMethod.Id,
        CardBrand = paymentMethod.CardBrand,
        Last4 = paymentMethod.Last4,
        Expiry = paymentMethod.Expiry,
        Alias = paymentMethod.Alias,
    };
}
