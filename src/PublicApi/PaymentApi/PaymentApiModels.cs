using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentApi;

// ---------- Request models ----------

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }   // state / province
    public string? AdminArea2 { get; set; }   // city
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

/// <summary>Card entry for a one-off payment or for saving. Card details are never stored or logged.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public AddressDto? ShipToAddress { get; set; }
}

public class PayOrderRequest
{
    /// <summary>Set from the route; any value in the body is ignored.</summary>
    public int OrderId { get; set; }

    /// <summary>Card details for a one-off payment, or ...</summary>
    public CardDto? Card { get; set; }

    /// <summary>... the id of one of the caller's saved cards. Provide exactly one.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class RefundRequest
{
    /// <summary>Set from the route; any value in the body is ignored.</summary>
    public int OrderId { get; set; }

    /// <summary>Amount to refund; omit for a full refund of the remaining balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key — repeating it never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? NoteToPayer { get; set; }
}

/// <summary>Operator action carrying only the order id (from the route).</summary>
public class OrderActionRequest
{
    public OrderActionRequest() { }
    public OrderActionRequest(int orderId) => OrderId = orderId;
    public int OrderId { get; set; }
}

public class DeletePaymentMethodRequest
{
    public DeletePaymentMethodRequest() { }
    public DeletePaymentMethodRequest(int paymentMethodId) => PaymentMethodId = paymentMethodId;
    public int PaymentMethodId { get; set; }
}

public class ReconciliationRequest
{
    public ReconciliationRequest() { }
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class SavePaymentMethodRequest
{
    public CardDto Card { get; set; } = new();
}

// ---------- Response models ----------

public class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class RefundResponseItem
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentResponse
{
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PaymentSource { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundResponseItem> Refunds { get; set; } = new();
}

public class OrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemResponse> Items { get; set; } = new();
    public PaymentResponse? Payment { get; set; }
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public OrderResponse Order { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<OrderResponse> Orders { get; set; } = new();
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public PaymentResponse Payment { get; set; } = new();
}

public class PaymentMethodResponse
{
    public int Id { get; set; }
    public string? CardBrand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class CreatePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodResponse PaymentMethod { get; set; } = new();
}

public class PaymentMethodsResponse
{
    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}

// ---------- Reconciliation ----------

public class ReconciliationLineResponse
{
    public string MatchState { get; set; } = string.Empty;
    public int? EshopOrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? CaptureId { get; set; }
    public string? InvoiceId { get; set; }
    public decimal? EshopAmount { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? PayPalStatus { get; set; }
    public DateTimeOffset? Date { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int PayPalTransactionCount { get; set; }
    public int EshopOrderCount { get; set; }
    public int MatchedCount { get; set; }
    public List<ReconciliationLineResponse> Matched { get; set; } = new();
    public List<ReconciliationLineResponse> OnlyInPayPal { get; set; } = new();
    public List<ReconciliationLineResponse> OnlyInEshop { get; set; } = new();
}
