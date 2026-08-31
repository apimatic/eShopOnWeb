using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PlaceOrderRequest
{
    public List<PlaceOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest ShippingAddress { get; set; } = new();
}

public sealed class PlaceOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public sealed class CardRequest
{
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public BillingAddressRequest BillingAddress { get; set; } = new();
}

public sealed class BillingAddressRequest
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
}

public sealed class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public sealed class RefundOrderRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public sealed record PlaceOrderResponse(int OrderId, OrderResponse Order);
public sealed record SavePaymentMethodResponse(int PaymentMethodId, PaymentMethodResponse PaymentMethod);
public sealed record RefundOrderResponse(string RefundId, OrderResponse Order);
public sealed record PaymentMethodResponse(int PaymentMethodId, string Brand, string Last4, string Expiry);

public sealed class OrderResponse
{
    public int OrderId { get; init; }
    public DateTimeOffset OrderDate { get; init; }
    public decimal Total { get; init; }
    public string? Currency { get; init; }
    public string Status { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? AuthorizationExpiresAt { get; init; }
    public decimal? AuthorizedAmount { get; init; }
    public string? CaptureId { get; init; }
    public string? CaptureStatus { get; init; }
    public decimal? CapturedAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public decimal RefundedAmount { get; init; }
    public IReadOnlyList<OrderItemResponse> Items { get; init; } = Array.Empty<OrderItemResponse>();
    public IReadOnlyList<RefundResponse> Refunds { get; init; } = Array.Empty<RefundResponse>();
}

public sealed record OrderItemResponse(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record RefundResponse(string RefundId, decimal Amount, string Currency, string Status, DateTimeOffset CreatedAt);

public sealed class ReconciliationResponse
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<ReconciliationTransactionResponse> Transactions { get; init; } = Array.Empty<ReconciliationTransactionResponse>();
    public IReadOnlyList<ReconciliationOrderResponse> EshopOnlyOrders { get; init; } = Array.Empty<ReconciliationOrderResponse>();
}

public sealed record ReconciliationTransactionResponse(string PayPalTransactionId, string? PayPalReferenceId,
    string? TransactionEventCode,
    string? InvoiceId, DateTimeOffset? TransactionTime, decimal? Amount, string? Currency, decimal? Fee,
    string? Status, string MatchStatus, int? OrderId);
public sealed record ReconciliationOrderResponse(int OrderId, string PaymentReference, string PaymentStatus,
    string? PayPalOrderId, string? AuthorizationId, string? CaptureId);
