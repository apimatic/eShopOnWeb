using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Save a card for the signed-in shopper.</summary>
public class CreatePaymentMethodRequest
{
    public CardRequest Card { get; set; } = new();
}

/// <summary>
/// A saved card, described safely enough to recognise which card it is — never full card details.
/// The created identifier is exposed as the top-level <see cref="PaymentMethodId"/>.
/// </summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto From(SavedCard card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.Brand,
        Last4 = card.Last4,
        Expiry = card.Expiry,
        CardholderName = card.CardholderName,
        CreatedAt = card.CreatedAt
    };
}
