using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.PublicApi.PaymentShared;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest
{
    public CardDto Card { get; set; } = new();
}

public class DeletePaymentMethodRequest
{
    public DeletePaymentMethodRequest(int paymentMethodId) => PaymentMethodId = paymentMethodId;
    public int PaymentMethodId { get; }
}

/// <summary>A safe description of a saved card — never full card details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string? ExpiryYearMonth { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto From(SavedPaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        Brand = pm.CardBrand,
        LastFourDigits = pm.LastFourDigits,
        ExpiryYearMonth = pm.ExpiryYearMonth,
        CardholderName = pm.CardholderName,
        CreatedAt = pm.CreatedAt
    };
}

public class CreatePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string? ExpiryYearMonth { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ListPaymentMethodsResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();

    public static ListPaymentMethodsResponse From(IEnumerable<SavedPaymentMethod> cards) => new()
    {
        PaymentMethods = cards.Select(PaymentMethodDto.From).ToList()
    };
}
