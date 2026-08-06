using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// A safe descriptor of a saved card — enough for the shopper to recognise which card it is, and never
/// the full card number, CVV or anything sensitive.
/// </summary>
public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string CardholderName { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; }

    public static SavedCardDto From(SavedPaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        Brand = pm.CardBrand,
        Last4 = pm.Last4,
        ExpiryMonth = pm.ExpiryMonth,
        ExpiryYear = pm.ExpiryYear,
        CardholderName = pm.CardholderName,
        CreatedDate = pm.CreatedDate
    };
}
