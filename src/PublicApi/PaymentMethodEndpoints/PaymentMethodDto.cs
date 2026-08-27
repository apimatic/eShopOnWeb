using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Safe description of a saved card — enough for the shopper to recognise it,
/// never the full card details.
/// </summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto FromEntity(SavedPaymentMethod entity) => new PaymentMethodDto
    {
        PaymentMethodId = entity.Id,
        Brand = entity.Brand,
        LastDigits = entity.LastDigits,
        Expiry = entity.Expiry,
        CreatedAt = entity.CreatedAt
    };
}
