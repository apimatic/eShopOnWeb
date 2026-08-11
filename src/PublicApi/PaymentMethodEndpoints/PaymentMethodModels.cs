using System;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Card to vault for the signed-in shopper. Full card details go only to PayPal.</summary>
public class SavePaymentMethodRequest : CardRequestDto
{
    [JsonIgnore] public string BuyerId { get; set; } = default!;
}

public class ListPaymentMethodsRequest
{
    [JsonIgnore] public string BuyerId { get; set; } = default!;
}

public class DeletePaymentMethodRequest
{
    [JsonIgnore] public string BuyerId { get; set; } = default!;
    [JsonIgnore] public int PaymentMethodId { get; set; }
}

public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? CardBrand { get; set; }
    public string? Last4 { get; set; }
    public string? ExpiryMonth { get; set; }
    public string? ExpiryYear { get; set; }
    public string? CardholderName { get; set; }
}

/// <summary>Safe description of a saved card — enough to recognise it, never full card details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? CardBrand { get; set; }
    public string? Last4 { get; set; }
    public string? ExpiryMonth { get; set; }
    public string? ExpiryYear { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedDate { get; set; }

    public static PaymentMethodDto From(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        CardBrand = method.CardBrand,
        Last4 = method.Last4,
        ExpiryMonth = method.ExpiryMonth,
        ExpiryYear = method.ExpiryYear,
        CardholderName = method.CardholderName,
        CreatedDate = method.CreatedDate
    };
}
