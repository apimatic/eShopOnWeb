using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// A saved card described safely enough to recognise which card it is — never full card details.
/// </summary>
public record SavedCardView(
    int PaymentMethodId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName,
    DateTimeOffset CreatedAt)
{
    public static SavedCardView From(SavedCard c) =>
        new(c.Id, c.Brand, c.LastDigits, c.Expiry, c.CardholderName, c.CreatedAt);
}
