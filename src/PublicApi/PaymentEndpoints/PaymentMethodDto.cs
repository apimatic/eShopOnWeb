using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Safe description of a saved card — enough to recognise it, never the full number.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public string? Alias { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto From(PaymentMethod paymentMethod) => new()
    {
        PaymentMethodId = paymentMethod.Id,
        Brand = paymentMethod.Brand,
        Last4 = paymentMethod.Last4,
        Expiry = paymentMethod.Expiry,
        CardholderName = paymentMethod.CardholderName,
        Alias = paymentMethod.Alias,
        CreatedAt = paymentMethod.CreatedAt
    };
}
