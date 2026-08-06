using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// A saved card as returned to the caller — a safe descriptor only (brand, last four digits, expiry),
/// enough to recognise which card it is. Never carries full card details.
/// </summary>
public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedDate { get; set; }

    public static SavedCardDto From(SavedPaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        CardBrand = pm.CardBrand,
        LastFourDigits = pm.LastFourDigits,
        Expiry = pm.Expiry,
        CardholderName = pm.CardholderName,
        CreatedDate = pm.CreatedDate
    };
}
