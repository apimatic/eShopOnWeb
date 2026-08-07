using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ----- Save a card -----

/// <summary>Request to save a card. The body is the card itself.</summary>
public class SavePaymentMethodRequest : CardDto
{
}

/// <summary>
/// Response for a saved card. <c>PaymentMethodId</c> is a top-level identifier for follow-up calls.
/// The card is described safely (brand + last 4 + expiry) — never full details.
/// </summary>
public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string Last4 { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? Alias { get; set; }
}

// ----- List saved cards -----

public class ListPaymentMethodsResponse
{
    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string Last4 { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? Alias { get; set; }

    public static SavedCardDto From(SavedCard card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.Brand,
        Last4 = card.Last4,
        Expiry = card.ExpiryMonthYear,
        Alias = card.Alias
    };
}
