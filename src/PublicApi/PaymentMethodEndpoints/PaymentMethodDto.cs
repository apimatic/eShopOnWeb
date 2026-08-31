using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Safe description of a saved card: enough for the shopper to recognise it, never
/// full card details.
/// </summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto From(SavedCard card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.Brand,
        LastDigits = card.LastDigits,
        Expiry = card.Expiry,
        CardholderName = card.CardholderName,
        CreatedAt = card.CreatedAt
    };
}
