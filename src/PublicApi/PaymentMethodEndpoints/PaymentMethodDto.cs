using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = default!;
    public string LastDigits { get; set; } = default!;
    /// <summary>"YYYY-MM".</summary>
    public string Expiry { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto FromEntity(PaymentMethod paymentMethod) => new()
    {
        PaymentMethodId = paymentMethod.Id,
        CardBrand = paymentMethod.CardBrand,
        LastDigits = paymentMethod.LastDigits,
        Expiry = paymentMethod.Expiry,
        CreatedAt = paymentMethod.CreatedAt
    };
}
