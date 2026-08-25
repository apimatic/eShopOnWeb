using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

// Safe-to-display description of a saved card. Never carries the full card number.
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto FromEntity(PaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        Brand = pm.Brand,
        Last4 = pm.Last4,
        ExpiryMonth = pm.ExpiryMonth,
        ExpiryYear = pm.ExpiryYear,
        CreatedAt = pm.CreatedAt
    };
}
