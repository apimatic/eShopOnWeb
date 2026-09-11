using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CardDto
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in YYYY-MM.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public GatewayCardDetails ToGateway() => new()
    {
        Number = Number,
        Expiry = Expiry,
        SecurityCode = SecurityCode,
        CardholderName = CardholderName,
        BillingAddress = BillingAddress?.ToGateway()
    };
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    /// <summary>City.</summary>
    public string? AdminArea2 { get; set; }
    /// <summary>State / province.</summary>
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    public string? CountryCode { get; set; }

    public GatewayBillingAddress ToGateway() => new()
    {
        AddressLine1 = AddressLine1,
        AddressLine2 = AddressLine2,
        AdminArea2 = AdminArea2,
        AdminArea1 = AdminArea1,
        PostalCode = PostalCode,
        CountryCode = CountryCode
    };
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShipToAddressDto? ShipToAddress { get; set; }
}

public class PayOrderRequest
{
    public int OrderId { get; set; }
    public CardDto? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

public class RefundOrderRequest
{
    public int OrderId { get; set; }
    public decimal? Amount { get; set; }
    /// <summary>Caller-supplied idempotency key; repeating under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
