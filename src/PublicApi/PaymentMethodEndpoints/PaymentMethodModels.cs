using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest
{
    public CardDto Card { get; set; } = new();

    internal string BuyerId { get; set; } = string.Empty;
}

/// <summary>A saved card described safely enough to recognise it — never full card details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Description { get; set; } = string.Empty; // e.g. "VISA ****1111"
    public string CardBrand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardHolderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto From(PaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        Description = pm.Describe(),
        CardBrand = pm.CardBrand,
        LastFourDigits = pm.LastFourDigits,
        Expiry = pm.Expiry,
        CardHolderName = pm.CardHolderName,
        CreatedAt = pm.CreatedAt
    };
}

/// <summary>Response for saving a card; exposes the new id as a top-level field.</summary>
public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}
