using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public sealed class AddressInput
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}

/// <summary>
/// Card data used only to tokenize the card with Finix. It is never persisted
/// and must never be written to logs.
/// </summary>
public sealed class CardDetailsInput
{
    public string? Number { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public AddressInput? Address { get; set; }
}

public sealed class OrderLineInput
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class CreatedOrderResult
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public IReadOnlyList<OrderLineResult> Items { get; set; } = Array.Empty<OrderLineResult>();
}

public sealed class OrderLineResult
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class PaymentActionResult
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? PaymentInstrumentId { get; set; }
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? Message { get; set; }
}

public sealed class FulfilmentResult
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? CaptureState { get; set; }
    public long CapturedAmountCents { get; set; }
    public decimal CapturedAmount { get; set; }
    public long FinixFeeCents { get; set; }
    public decimal FinixFee { get; set; }
    public long NetProceedsCents { get; set; }
    public decimal NetProceeds { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? Message { get; set; }
}

public sealed class CancelResult
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? VoidState { get; set; }
    public string? Message { get; set; }
}

public sealed class RefundResult
{
    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public string? FinixRefundTransferId { get; set; }
    public long RefundedAmountCents { get; set; }
    public decimal RefundedAmount { get; set; }
    public long TotalRefundedCents { get; set; }
    public decimal TotalRefunded { get; set; }
    public long RemainingRefundableCents { get; set; }
    public decimal RemainingRefundable { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? Message { get; set; }
}

public sealed class OrderRefundSummary
{
    public int RefundId { get; set; }
    public string? FinixRefundTransferId { get; set; }
    public decimal Amount { get; set; }
    public long AmountCents { get; set; }
    public string State { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class OrderSummaryResult
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? CaptureState { get; set; }
    public long CapturedAmountCents { get; set; }
    public decimal CapturedAmount { get; set; }
    public long FinixFeeCents { get; set; }
    public decimal FinixFee { get; set; }
    public long NetProceedsCents { get; set; }
    public decimal NetProceeds { get; set; }
    public long RefundedAmountCents { get; set; }
    public decimal RefundedAmount { get; set; }
    public long RemainingRefundableCents { get; set; }
    public decimal RemainingRefundable { get; set; }
    public string? PaymentFailureMessage { get; set; }
    public IReadOnlyList<OrderLineResult> Items { get; set; } = Array.Empty<OrderLineResult>();
    public IReadOnlyList<OrderRefundSummary> Refunds { get; set; } = Array.Empty<OrderRefundSummary>();
}

public sealed class SavedPaymentMethodResult
{
    public int PaymentMethodId { get; set; }
    public string PaymentInstrumentId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FinixTransactionRecord
{
    public string Id { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? State { get; set; }
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? Merchant { get; set; }
    public string? OperationKey { get; set; }
    public string? ParentTransfer { get; set; }
    public int? OrderId { get; set; }
    public string? MatchType { get; set; }
    public bool Matched { get; set; }
}

public sealed class FinixAuthorizationRecord
{
    public string Id { get; set; } = string.Empty;
    public string? State { get; set; }
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public bool IsVoid { get; set; }
    public string? VoidState { get; set; }
    public string? TransferId { get; set; }
    public int? OrderId { get; set; }
    public bool Matched { get; set; }
}

public sealed class ReconciliationOrderRecord
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureTransferId { get; set; }
    public IReadOnlyList<string> FinixTransferIds { get; set; } = Array.Empty<string>();
    public bool Matched { get; set; }
}

public sealed class ReconciliationResult
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int FinixTransactionCount { get; set; }
    public int FinixAuthorizationCount { get; set; }
    public int OrderCount { get; set; }
    public int UnmatchedFinixTransactionCount { get; set; }
    public int UnmatchedOrderCount { get; set; }
    public IReadOnlyList<FinixTransactionRecord> FinixTransactions { get; set; } = Array.Empty<FinixTransactionRecord>();
    public IReadOnlyList<FinixAuthorizationRecord> FinixAuthorizations { get; set; } = Array.Empty<FinixAuthorizationRecord>();
    public IReadOnlyList<ReconciliationOrderRecord> Orders { get; set; } = Array.Empty<ReconciliationOrderRecord>();
    public IReadOnlyList<FinixTransactionRecord> UnmatchedFinixTransactions { get; set; } = Array.Empty<FinixTransactionRecord>();
    public IReadOnlyList<ReconciliationOrderRecord> UnmatchedOrders { get; set; } = Array.Empty<ReconciliationOrderRecord>();
}
