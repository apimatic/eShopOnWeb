using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
}

public class PayOrderRequest : BaseRequest
{
    public int? PaymentMethodId { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public CardRequest? Card { get; set; }
    public string? Number { get; set; }
    public string? CardNumber { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Cvv { get; set; }
    public string? Cvc { get; set; }
    public string? Name { get; set; }
    public FinixAddress? Address { get; set; }
}

public class CardRequest
{
    public string? Number { get; set; }
    public string? CardNumber { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Cvv { get; set; }
    public string? Cvc { get; set; }
    public string? Name { get; set; }
    public FinixAddress? Address { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public decimal AmountAuthorized { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PaymentInstrumentId { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? CaptureTransferId { get; set; }
    public string? CaptureTransferState { get; set; }
    public decimal CapturedAmount { get; set; }
    public decimal FinixFee { get; set; }
    public decimal NetProceeds { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationState { get; set; }
    public bool Released { get; set; }
}

public class RefundOrderRequest : BaseRequest
{
    public decimal Amount { get; set; }
    public long? AmountCents { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string RefundState { get; set; } = string.Empty;
    public string? FinixRefundId { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
}

public class MyOrdersRequest : BaseRequest
{
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public OrderPaymentDto? Payment { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new();
}

public class MyOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderPaymentDto
{
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? CaptureTransferState { get; set; }
    public decimal? AmountAuthorized { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? FinixFee { get; set; }
    public decimal? NetProceeds { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RefundableAmount { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public int RefundId { get; set; }
    public decimal Amount { get; set; }
    public string State { get; set; } = string.Empty;
    public string? FinixRefundId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransactionDto> FinixTransactions { get; set; } = new();
    public List<ReconciliationTransactionDto> UnmatchedFinixTransactions { get; set; } = new();
    public List<EshopPaymentDto> EshopOrdersWithoutFinixRecord { get; set; } = new();
    public ReconciliationSummaryDto Summary { get; set; } = new();
}

public class ReconciliationTransactionDto
{
    public string FinixId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? State { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int? OrderId { get; set; }
    public bool Matched { get; set; }
    public string? MatchType { get; set; }
}

public class EshopPaymentDto
{
    public int OrderId { get; set; }
    public int? PaymentId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureTransferId { get; set; }
    public decimal? Amount { get; set; }
    public decimal? CapturedAmount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
}

public class ReconciliationSummaryDto
{
    public int FinixTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public int UnmatchedFinixCount { get; set; }
    public int EshopOnlyCount { get; set; }
}
