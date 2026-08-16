using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest
{
    /// <summary>The card to save. Full details are sent to PayPal to vault; never stored by this app.</summary>
    public CardRequestDto Card { get; set; } = new();

    public string? Alias { get; set; }
}

/// <summary>Safe description of a saved card — never full card details.</summary>
public class SavedCardDto
{
    public int Id { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string ExpiryMonth { get; set; } = string.Empty;
    public string ExpiryYear { get; set; } = string.Empty;
    public string? Alias { get; set; }
    public string Description { get; set; } = string.Empty;

    public static SavedCardDto From(SavedCard card) => new()
    {
        Id = card.Id,
        Brand = card.Brand,
        Last4 = card.Last4,
        ExpiryMonth = card.ExpiryMonth,
        ExpiryYear = card.ExpiryYear,
        Alias = card.Alias,
        Description = card.Describe(),
    };
}

public class SavePaymentMethodResponse
{
    /// <summary>Top-level identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string ExpiryMonth { get; set; } = string.Empty;
    public string ExpiryYear { get; set; } = string.Empty;
    public string? Alias { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class ListPaymentMethodsResponse
{
    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodRequest
{
    public int PaymentMethodId { get; set; }
}
