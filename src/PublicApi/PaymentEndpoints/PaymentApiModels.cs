using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- shared card DTOs (never persisted or logged) ----

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;

    public PayPalBillingAddress ToDomain() =>
        new(AddressLine1, AddressLine2, City, State, PostalCode, CountryCode);
}

public class CardDto
{
    /// <summary>Card number. Sandbox test card: 4111111111111111.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM form (e.g. 2030-01).</summary>
    public string Expiry { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToDomain() =>
        new(Number, Expiry, SecurityCode, CardholderName, BillingAddress?.ToDomain());
}

// ---- create order ----

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public ShippingAddressRequest ToDomain() => new(Street, City, State, Country, ZipCode);
}

public class CreateOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipTo { get; set; }
}

public class CreateOrderResponse
{
    public CreateOrderResponse(int orderId, PaymentDetailsViewModel payment)
    {
        OrderId = orderId;
        Payment = payment;
    }

    /// <summary>Top-level identifier of the created order.</summary>
    public int OrderId { get; set; }
    public PaymentDetailsViewModel Payment { get; set; }
}

// ---- pay ----

public class PayOrderRequest
{
    /// <summary>A raw one-off card. Provide this OR <see cref="PaymentMethodId"/>, not both.</summary>
    public CardDto? Card { get; set; }

    /// <summary>One of the caller's saved cards to pay with. Provide this OR <see cref="Card"/>, not both.</summary>
    public int? PaymentMethodId { get; set; }

    public PayInstruction ToInstruction() => new()
    {
        Card = Card?.ToDomain(),
        PaymentMethodId = PaymentMethodId
    };
}

// ---- refund ----

public class RefundOrderRequest
{
    /// <summary>Amount to refund; omit for a full refund of the remaining captured balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating a request under the same key does not refund twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    public RefundOrderResponse(string refundId, RefundViewModel refund)
    {
        RefundId = refundId;
        Refund = refund;
    }

    /// <summary>Top-level identifier of the created refund (PayPal's refund id).</summary>
    public string RefundId { get; set; }
    public RefundViewModel Refund { get; set; }
}

// ---- payment methods ----

public class SavePaymentMethodRequest
{
    public CardDto Card { get; set; } = new();
}

public class SavePaymentMethodResponse
{
    public SavePaymentMethodResponse(int paymentMethodId, PaymentMethodViewModel card)
    {
        PaymentMethodId = paymentMethodId;
        Card = card;
    }

    /// <summary>Top-level identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }
    public PaymentMethodViewModel Card { get; set; }
}
