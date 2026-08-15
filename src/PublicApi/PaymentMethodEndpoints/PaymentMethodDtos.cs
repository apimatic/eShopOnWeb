using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Safe description of a saved card — enough to recognise it, never full card details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset SavedAt { get; set; }

    public static PaymentMethodDto From(PaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        Brand = pm.CardBrand,
        LastFourDigits = pm.LastFourDigits,
        Expiry = pm.Expiry,
        CardholderName = pm.CardholderName,
        SavedAt = pm.CreatedAt
    };
}
