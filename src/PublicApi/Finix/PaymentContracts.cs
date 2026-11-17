using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Finix;

public sealed class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipToAddress { get; set; }
}

public sealed class CreateOrderItemRequest
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

public sealed class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public sealed class PayOrderRequest : BaseRequest
{
    public string? PaymentMethodId { get; set; }
    public CardDetailsRequest? Card { get; set; }
}

public sealed class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public sealed class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureTransferId { get; set; }
    public long? CapturedAmountCents { get; set; }
    public long? FeeCents { get; set; }
    public long? NetCents { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public sealed class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
}

public sealed class RefundOrderRequest : BaseRequest
{
    public decimal Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public long AmountCents { get; set; }
    public long RefundedCents { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<OrderSummary> Orders { get; set; } = new();
}

public sealed class OrderSummary
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public ShippingAddressRequest ShipToAddress { get; set; } = new();
    public List<OrderItemSummary> Items { get; set; } = new();
    public PaymentSummary? Payment { get; set; }
}

public sealed class OrderItemSummary
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public sealed class PaymentSummary
{
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? CaptureTransferId { get; set; }
    public long? CapturedAmountCents { get; set; }
    public long? FeeCents { get; set; }
    public long? NetCents { get; set; }
    public long RefundedCents { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
}

public sealed class SavePaymentMethodRequest : BaseRequest
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public SavePaymentMethodResponse() { }

    public string PaymentMethodId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? Name { get; set; }
}

public sealed class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public ListPaymentMethodsResponse() { }

    public List<PaymentMethodSummary> PaymentMethods { get; set; } = new();
}

public sealed class PaymentMethodSummary
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public DeletePaymentMethodResponse() { }

    public string PaymentMethodId { get; set; } = string.Empty;
    public bool Deleted { get; set; }
}

public sealed class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public bool Truncated { get; set; }
    public string? NextCursor { get; set; }
    public List<ReconciliationFinixTransaction> FinixTransactions { get; set; } = new();
    public List<ReconciliationMatch> Matched { get; set; } = new();
    public List<ReconciliationFinixTransaction> UnmatchedFinix { get; set; } = new();
    public List<ReconciliationEshopRecord> UnmatchedEshop { get; set; } = new();
}

public sealed class ReconciliationFinixTransaction
{
    public string Kind { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string? State { get; set; }
    public string? Type { get; set; }
    public long? AmountCents { get; set; }
    public string? Currency { get; set; }
    public string? Merchant { get; set; }
    public string? ParentTransfer { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class ReconciliationMatch
{
    public string FinixId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string MatchType { get; set; } = string.Empty;
}

public sealed class ReconciliationEshopRecord
{
    public int OrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? RefundId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long AmountCents { get; set; }
}
