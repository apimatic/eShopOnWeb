using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>A safe descriptor of a saved card — enough to recognise it, never full card details.</summary>
public class SavedCardView
{
    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static SavedCardView From(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        CardBrand = method.CardBrand,
        LastFourDigits = method.LastFourDigits,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName,
        CreatedAt = method.CreatedAt
    };
}
