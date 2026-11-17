using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class AddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public sealed class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class CreateOrderRequest
{
    public List<OrderLineRequest>? Items { get; set; }
    public AddressRequest? ShipToAddress { get; set; }
}

public sealed class CreateOrderResponse
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class CardRequest
{
    public string? Number { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
}

public sealed class PayOrderRequest
{
    public string? SavedCardId { get; set; }
    public CardRequest? Card { get; set; }
}

public class PaymentSummaryResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? CaptureState { get; set; }
    public long CapturedAmountCents { get; set; }
    public long FeeAmountCents { get; set; }
    public long NetAmountCents { get; set; }
    public long RefundedAmountCents { get; set; }
    public long RefundableAmountCents { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PayOrderResponse : PaymentSummaryResponse
{
}

public sealed class FulfilOrderResponse : PaymentSummaryResponse
{
    public bool FeeComplete { get; set; }
}

public sealed class CancelOrderResponse : PaymentSummaryResponse
{
    public string? VoidState { get; set; }
}

public sealed class RefundRequest
{
    public long? AmountCents { get; set; }
    public string? IdempotencyKey { get; set; }
}

public sealed class RefundResponse
{
    public int OrderId { get; set; }
    public string RefundId { get; set; } = string.Empty;
    public string? FinixReversalId { get; set; }
    public long AmountCents { get; set; }
    public long RefundedAmountCents { get; set; }
    public long RefundableAmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class MyOrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public sealed class MyOrderResponse
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public long CapturedAmountCents { get; set; }
    public long RefundedAmountCents { get; set; }
    public long RefundableAmountCents { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureTransferId { get; set; }
    public List<MyOrderItemResponse> Items { get; set; } = new();
}

public sealed class SavePaymentMethodRequest
{
    public string? Number { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
}

public sealed class SavePaymentMethodResponse
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string LastFour { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
}

public sealed class PaymentMethodResponse
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string LastFour { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ReconciliationTransactionResponse
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? State { get; set; }
    public long AmountCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? IdempotencyId { get; set; }
    public string? ParentTransferId { get; set; }
    public int? MatchedOrderId { get; set; }
}

public sealed class UnmatchedEshopRecordResponse
{
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public bool Truncated { get; set; }
    public List<ReconciliationTransactionResponse> FinixTransactions { get; set; } = new();
    public List<ReconciliationTransactionResponse> UnmatchedFinix { get; set; } = new();
    public List<UnmatchedEshopRecordResponse> UnmatchedEshop { get; set; } = new();
}
