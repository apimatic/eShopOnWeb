using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>A safe description of a saved card — enough to recognise it, never full card details.</summary>
public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? CardHolderName { get; set; }
    public string? Alias { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static SavedCardDto From(PaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        Last4 = method.Last4,
        Expiry = method.Expiry,
        CardHolderName = method.CardHolderName,
        Alias = method.Alias,
        CreatedAt = method.CreatedAt
    };
}
