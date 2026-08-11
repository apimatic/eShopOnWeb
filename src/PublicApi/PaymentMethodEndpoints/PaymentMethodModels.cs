using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest
{
    public CardRequest Card { get; set; } = new();
    public string? Label { get; set; }
}

/// <summary>Response for a saved card — a safe description only, never full card details.</summary>
public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string Last4 { get; set; } = string.Empty;
    public string? ExpiryMonth { get; set; }
    public string? ExpiryYear { get; set; }
    public string? Label { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string Last4 { get; set; } = string.Empty;
    public string? ExpiryMonth { get; set; }
    public string? ExpiryYear { get; set; }
    public string? Label { get; set; }
}

public class ListPaymentMethodsResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public static class PaymentMethodMapping
{
    public static PaymentMethodDto ToDto(this SavedCard card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.Brand,
        Last4 = card.Last4,
        ExpiryMonth = card.ExpiryMonth,
        ExpiryYear = card.ExpiryYear,
        Label = card.Label
    };
}
