using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>A safe description of a saved card — never full card details.</summary>
public class PaymentMethodDto
{
    public int Id { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public string? Expiry { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto From(PaymentMethod paymentMethod) => new()
    {
        Id = paymentMethod.Id,
        CardBrand = paymentMethod.CardBrand,
        LastFourDigits = paymentMethod.LastFourDigits,
        CardholderName = paymentMethod.CardholderName,
        Expiry = paymentMethod.Expiry,
        CreatedAt = paymentMethod.CreatedAt
    };
}
