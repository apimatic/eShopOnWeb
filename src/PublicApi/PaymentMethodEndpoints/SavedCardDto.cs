using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static SavedCardDto FromEntity(SavedCard card)
    {
        return new SavedCardDto
        {
            PaymentMethodId = card.Id,
            Brand = card.Brand,
            LastDigits = card.LastDigits,
            Expiry = card.Expiry,
            CardholderName = card.CardholderName,
            CreatedAt = card.CreatedAt
        };
    }
}
