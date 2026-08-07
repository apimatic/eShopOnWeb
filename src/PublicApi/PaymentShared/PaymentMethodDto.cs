using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentShared;

/// <summary>
/// A shopper-facing view of a saved card: enough to recognise it (brand, last four, expiry)
/// but never the full card details, which live only in PayPal's vault.
/// </summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? CardBrand { get; set; }
    public string? LastFourDigits { get; set; }

    /// <summary>Card expiry in <c>YYYY-MM</c> form.</summary>
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedDate { get; set; }

    public static PaymentMethodDto FromEntity(SavedPaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        CardBrand = pm.CardBrand,
        LastFourDigits = pm.LastFourDigits,
        Expiry = pm.CardExpiry,
        CardholderName = pm.CardholderName,
        CreatedDate = pm.CreatedDate
    };
}
