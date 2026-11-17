using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int? Id { get; set; }
    public int? ProductId { get; set; }
    public int Quantity { get; set; }

    public int ResolveCatalogItemId() => CatalogItemId > 0 ? CatalogItemId : (Id ?? ProductId ?? 0);
}

public class ShippingAddressDto
{
    public string? Street { get; set; }
    public string? Line1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
    public string? PostalCode { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineRequest>? Items { get; set; }
    public List<OrderLineRequest>? OrderItems { get; set; }
    public List<OrderLineRequest>? Lines { get; set; }
    public ShippingAddressDto? ShippingAddress { get; set; }
    public ShippingAddressDto? Address { get; set; }

    public List<OrderLineRequest>? ResolveItems() => Items ?? OrderItems ?? Lines;
}

public class CardAddressDto
{
    public string? Line1 { get; set; }
    public string? Street { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? ZipCode { get; set; }
    public string? Country { get; set; }
}

public class CardDetailsDto
{
    public string? Number { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Cvc { get; set; }
    public string? Cvv { get; set; }
    public string? Name { get; set; }
    public CardAddressDto? Address { get; set; }
}

public class PayOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    public int? PaymentMethodId { get; set; }
    public int? SavedCardId { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public int? CardId { get; set; }
    public string? PaymentInstrumentId { get; set; }
    public CardDetailsDto? Card { get; set; }

    public string? Number { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Cvc { get; set; }
    public string? Cvv { get; set; }
    public string? Name { get; set; }
    public CardAddressDto? Address { get; set; }
}

public class SavePaymentMethodRequest : BaseRequest
{
    public CardDetailsDto? Card { get; set; }

    public string? Number { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Cvc { get; set; }
    public string? Cvv { get; set; }
    public string? Name { get; set; }
    public CardAddressDto? Address { get; set; }
}

public class RefundOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    public long? AmountCents { get; set; }
    public long? RefundAmountCents { get; set; }
    public decimal? Amount { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? IdempotencyId { get; set; }
}

public class OrderLineResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderLineResponse> Items { get; set; } = new();
}

public class PaymentResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public decimal Amount { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool Idempotent { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? CaptureState { get; set; }
    public decimal CapturedAmount { get; set; }
    public long CapturedAmountCents { get; set; }
    public decimal Fee { get; set; }
    public long FeeCents { get; set; }
    public decimal Net { get; set; }
    public long NetCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public bool Idempotent { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? VoidState { get; set; }
    public decimal ReleasedAmount { get; set; }
    public long ReleasedAmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool Idempotent { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public int RefundId { get; set; }
    public string? FinixRefundId { get; set; }
    public decimal Amount { get; set; }
    public long AmountCents { get; set; }
    public decimal RefundedTotal { get; set; }
    public long RefundedTotalCents { get; set; }
    public decimal RefundableRemaining { get; set; }
    public long RefundableRemainingCents { get; set; }
    public decimal CapturedAmount { get; set; }
    public long CapturedAmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? State { get; set; }
    public bool Idempotent { get; set; }
}

public class RefundDto
{
    public int RefundId { get; set; }
    public int Id { get; set; }
    public string? FinixRefundId { get; set; }
    public decimal Amount { get; set; }
    public long AmountCents { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? State { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public int Id { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? CaptureState { get; set; }
    public decimal? CapturedAmount { get; set; }
    public long? CapturedAmountCents { get; set; }
    public decimal? Fee { get; set; }
    public long? FeeCents { get; set; }
    public decimal? Net { get; set; }
    public long? NetCents { get; set; }
    public decimal RefundedAmount { get; set; }
    public long RefundedAmountCents { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
    public List<OrderLineResponse> Items { get; set; } = new();
}

public class MyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public int Id { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public bool Deleted { get; set; }
}

public class ReconciliationRequest
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}

public class ReconciliationTransactionDto
{
    public string FinixId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? CreatedAt { get; set; }
    public int? OrderId { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
    public string? OperationKey { get; set; }
    public string? ParentId { get; set; }
}

public class ReconciliationOrderDto
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string MatchStatus { get; set; } = string.Empty;
}

public class ReconciliationSummaryDto
{
    public int FinixTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public int FinixOnlyCount { get; set; }
    public int EshopOrderCount { get; set; }
    public int EshopOnlyCount { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransactionDto> FinixTransactions { get; set; } = new();
    public List<ReconciliationOrderDto> EshopOrders { get; set; } = new();
    public List<ReconciliationTransactionDto> UnmatchedFinixTransactions { get; set; } = new();
    public List<ReconciliationOrderDto> UnmatchedEshopOrders { get; set; } = new();
    public ReconciliationSummaryDto Summary { get; set; } = new();
}
