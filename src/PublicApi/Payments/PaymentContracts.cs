using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
    public string? Email { get; set; }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public long TotalCents { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class CardAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public CardAddressRequest? Address { get; set; }
}

public class PayOrderRequest
{
    public string? PaymentMethodId { get; set; }
    public CardRequest? Card { get; set; }
    public string? Email { get; set; }
}

public class PayOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? HoldExpiresAt { get; set; }
    public string? PaymentMethodId { get; set; }
}

public class FulfilOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? TransferState { get; set; }
    public long CapturedAmountCents { get; set; }
    public long? FeeCents { get; set; }
    public long? NetProceedsCents { get; set; }
    public bool FeePending { get; set; }
}

public class CancelOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class RefundOrderRequest
{
    public long? AmountCents { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public long AmountCents { get; set; }
    public string Status { get; set; } = string.Empty;
    public long TotalRefundedCents { get; set; }
    public long RemainingRefundableCents { get; set; }
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? TransferState { get; set; }
    public long CapturedCents { get; set; }
    public long RefundedCents { get; set; }
    public long RefundableCents { get; set; }
    public int ItemCount { get; set; }
}

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class FinixTransactionDto
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Status { get; set; }
    public long? AmountCents { get; set; }
    public long? FeeCents { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? ParentTransferId { get; set; }
    public string? IdempotencyId { get; set; }
    public int? MatchedOrderId { get; set; }
    public string MatchStatus { get; set; } = "unmatched";
}

public class EshopOnlyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public long TotalCents { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureTransferId { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<FinixTransactionDto> FinixTransactions { get; set; } = new();
    public List<EshopOnlyOrderDto> EshopOnlyOrders { get; set; } = new();
    public bool Truncated { get; set; }
    public string? NextCursor { get; set; }
    public string? AuthorizationNextCursor { get; set; }
}

public class CreatePaymentMethodResponse
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? LastFour { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
}

public class PaymentMethodDto
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? LastFour { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ListPaymentMethodsResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}
