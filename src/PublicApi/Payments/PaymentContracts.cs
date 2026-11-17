using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public sealed class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class ShipToAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public sealed class CardDetailsRequest
{
    public string? Number { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
}

public sealed class PayOrderRequest
{
    public int OrderId { get; set; }
    public CardDetailsRequest? Card { get; set; }
    public string? PaymentMethodId { get; set; }
}

public sealed class FulfilOrderRequest
{
    public int OrderId { get; set; }
}

public sealed class CancelOrderRequest
{
    public int OrderId { get; set; }
}

public sealed class MyOrdersRequest
{
}

public sealed class ReconciliationRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public sealed class RefundOrderRequest
{
    public int OrderId { get; set; }
    public long? Amount { get; set; }
    public string? IdempotencyKey { get; set; }
}

public sealed class ListPaymentMethodsRequest
{
}

public sealed class DeletePaymentMethodRequest
{
    public string PaymentMethodId { get; set; } = string.Empty;
}


public sealed class SavePaymentMethodRequest
{
    public string? Number { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
}

public sealed class CreateOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long TotalAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemResponse> Items { get; set; } = new();
}

public sealed class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public sealed class PayOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public long? HeldAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
}

public sealed class FulfilOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long? CapturedAmount { get; set; }
    public decimal? FinixFee { get; set; }
    public decimal? NetProceeds { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? CaptureTransferId { get; set; }
    public bool FeesFinalized { get; set; }
    public string? Explanation { get; set; }
}

public sealed class CancelOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? VoidState { get; set; }
    public bool Released { get; set; }
}

public sealed class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public long Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? FinixRefundId { get; set; }
    public string? Explanation { get; set; }
}

public sealed class MyOrdersResponse
{
    public List<MyOrderResponse> Orders { get; set; } = new();
}

public sealed class MyOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long TotalAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public PaymentStateResponse Payment { get; set; } = new();
}

public sealed class PaymentStateResponse
{
    public string? PaymentMethodId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? HoldStatus { get; set; }
    public long? HeldAmount { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? CaptureStatus { get; set; }
    public long? CapturedAmount { get; set; }
    public decimal? FinixFee { get; set; }
    public decimal? NetProceeds { get; set; }
    public bool FeesFinalized { get; set; }
    public long RefundedAmount { get; set; }
    public List<RefundStateResponse> Refunds { get; set; } = new();
}

public sealed class RefundStateResponse
{
    public string RefundId { get; set; } = string.Empty;
    public string? FinixRefundId { get; set; }
    public long Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationMatchResponse> Matched { get; set; } = new();
    public List<ProviderTransferResponse> ProviderOnly { get; set; } = new();
    public List<EshopOnlyResponse> EshopOnly { get; set; } = new();
    public bool Truncated { get; set; }
    public string? NextCursor { get; set; }
}

public sealed class ReconciliationMatchResponse
{
    public string FinixTransferId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string? RefundId { get; set; }
    public long Amount { get; set; }
    public string? State { get; set; }
    public string? Type { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class ProviderTransferResponse
{
    public string FinixTransferId { get; set; } = string.Empty;
    public long Amount { get; set; }
    public string? State { get; set; }
    public string? Type { get; set; }
    public string? ParentTransferId { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class EshopOnlyResponse
{
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? RefundId { get; set; }
    public string? FinixTransferId { get; set; }
    public long Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class SavePaymentMethodResponse
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
}

public sealed class ListPaymentMethodsResponse
{
    public List<SavePaymentMethodResponse> PaymentMethods { get; set; } = new();
}
