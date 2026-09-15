using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>A saved card described safely — brand, last four digits, expiry and name. Never full card details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; }

    public static PaymentMethodDto From(PaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        CardBrand = method.CardBrand,
        LastFourDigits = method.LastFourDigits,
        CardholderName = method.CardholderName,
        Expiry = method.Expiry,
        CreatedDate = method.CreatedDate
    };
}
