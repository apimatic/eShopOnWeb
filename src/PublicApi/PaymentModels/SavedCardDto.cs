using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Safe, shopper-facing description of a saved card. Carries only what is needed to recognise the
/// card (brand, last four digits, expiry) — never full card details.
/// </summary>
public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedDate { get; set; }

    public static SavedCardDto FromEntity(SavedPaymentMethod entity) => new()
    {
        PaymentMethodId = entity.Id,
        Brand = entity.CardBrand,
        Last4 = entity.LastFourDigits,
        Expiry = entity.CardExpiry,
        CardholderName = entity.CardholderName,
        CreatedDate = entity.CreatedDate
    };
}
