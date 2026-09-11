using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Body for POST /api/payment-methods — the card to vault. Full details are never stored.</summary>
public class SavePaymentMethodRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public AddressRequest? BillingAddress { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto From(SavedPaymentMethod m) => new()
    {
        PaymentMethodId = m.Id,
        Brand = m.Brand,
        Last4 = m.Last4,
        Expiry = m.Expiry,
        CardholderName = m.CardholderName,
        CreatedAt = m.CreatedAt,
    };
}

public class SavePaymentMethodResponse : PaymentMethodDto
{
}

public class ListPaymentMethodsResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}
