using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Safe-to-display description of a saved card. Never the full card number.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto FromPaymentMethod(PaymentMethod paymentMethod) => new PaymentMethodDto
    {
        PaymentMethodId = paymentMethod.Id,
        CardBrand = paymentMethod.CardBrand,
        LastDigits = paymentMethod.LastDigits,
        Expiry = paymentMethod.Expiry,
        CreatedAt = paymentMethod.CreatedAt
    };
}
