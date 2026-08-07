using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// A saved card described safely enough for the shopper to recognise it — brand, last four digits,
/// cardholder name and expiry. Never full card details.
/// </summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? CardBrand { get; set; }
    public string? Last4 { get; set; }
    public string? CardholderName { get; set; }
    public string? Expiry { get; set; }
    public System.DateTimeOffset CreatedDate { get; set; }

    public static PaymentMethodDto From(PaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        CardBrand = pm.CardBrand,
        Last4 = pm.LastFourDigits,
        CardholderName = pm.CardholderName,
        Expiry = pm.Expiry,
        CreatedDate = pm.CreatedDate
    };
}
