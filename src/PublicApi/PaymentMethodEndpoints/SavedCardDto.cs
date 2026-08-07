using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>A saved card described safely enough for the shopper to recognise it — never full card details.</summary>
public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }

    /// <summary>Last four digits of the card, e.g. "1111".</summary>
    public string? Last4 { get; set; }

    /// <summary>Expiry in YYYY-MM form.</summary>
    public string? Expiry { get; set; }

    public string? CardHolderName { get; set; }

    /// <summary>Optional shopper-supplied nickname for the card.</summary>
    public string? Alias { get; set; }

    public DateTimeOffset CreatedDate { get; set; }

    public static SavedCardDto FromEntity(PaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        Brand = pm.CardBrand,
        Last4 = pm.Last4,
        Expiry = pm.Expiry,
        CardHolderName = pm.CardHolderName,
        Alias = pm.Alias,
        CreatedDate = pm.CreatedDate
    };
}
