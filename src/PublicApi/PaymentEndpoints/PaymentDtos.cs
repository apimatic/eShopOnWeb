using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// --- Card ---

/// <summary>Raw card details supplied by the caller. Never stored by this app nor logged.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry as YYYY-MM.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }

    public CardDetails ToCardDetails() => new()
    {
        Number = Number,
        Expiry = Expiry,
        SecurityCode = SecurityCode,
        Name = Name,
        BillingAddressLine1 = BillingAddressLine1,
        BillingAddressLine2 = BillingAddressLine2,
        BillingCity = BillingCity,
        BillingState = BillingState,
        BillingPostalCode = BillingPostalCode,
        BillingCountryCode = BillingCountryCode
    };
}

// --- Requests ---

public class PlaceOrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PlaceOrderRequest
{
    public List<PlaceOrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class PayOrderRequest
{
    /// <summary>One-off card. Mutually exclusive with <see cref="SavedCardId"/>.</summary>
    public CardDto? Card { get; set; }
    /// <summary>Id of one of the caller's saved cards. Mutually exclusive with <see cref="Card"/>.</summary>
    public int? SavedCardId { get; set; }
}

public class CreateRefundRequest
{
    /// <summary>Amount to refund; omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }
    /// <summary>Caller-supplied idempotency key; repeating it never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class SavePaymentMethodRequest
{
    public CardDto Card { get; set; } = new();
    public string? Label { get; set; }
}

// --- Responses ---

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
}

public class CreateRefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? Label { get; set; }
}
