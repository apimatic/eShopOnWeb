using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Raw card details supplied to save (vault) a card. Never stored by the application.</summary>
public class CardDetailsPayload
{
    public string Number { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
}

/// <summary>A saved card described safely — brand, last four digits and expiry only, never a card number.</summary>
public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public DateTimeOffset CreatedDate { get; set; }

    public static SavedCardDto FromEntity(SavedPaymentMethod entity) => new()
    {
        PaymentMethodId = entity.Id,
        CardBrand = entity.CardBrand,
        LastFourDigits = entity.LastFourDigits,
        Expiry = entity.Expiry,
        CreatedDate = entity.CreatedDate
    };
}
