using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest
{
    public CardDetailsDto Card { get; set; } = new();
    public string? Alias { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}

/// <summary>
/// A saved card, described safely so the shopper can recognise it. Never carries full card details.
/// </summary>
public class SavedPaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string CardLast4 { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public string? Expiry { get; set; }
    public string? Alias { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static SavedPaymentMethodDto From(SavedPaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        CardBrand = pm.CardBrand,
        CardLast4 = pm.CardLast4,
        CardholderName = pm.CardholderName,
        Expiry = pm.CardExpiry,
        Alias = pm.Alias,
        CreatedAt = pm.CreatedAt
    };
}

public class SavePaymentMethodResponse : SavedPaymentMethodDto
{
}

public class ListPaymentMethodsResponse
{
    public List<SavedPaymentMethodDto> PaymentMethods { get; set; } = new();
}
