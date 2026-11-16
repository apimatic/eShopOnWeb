using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class OrderView
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal OrderTotal { get; set; }
    public decimal Total { get; set; }
    public int AmountCents { get; set; }

    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? PaymentInstrumentId { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureState { get; set; }
    public int? CapturedAmount { get; set; }
    public int? CapturedAmountCents { get; set; }
    public int? Fee { get; set; }
    public int? FeeCents { get; set; }
    public int? Net { get; set; }
    public int? NetCents { get; set; }
    public int RefundedAmount { get; set; }
    public int RefundedAmountCents { get; set; }
    public string? PaymentFailureMessage { get; set; }

    public List<OrderItemView> Items { get; set; } = new();
    public PaymentView Payment { get; set; } = new();
}

public sealed class OrderItemView
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class PaymentView
{
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public int AmountCents { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? PaymentInstrumentId { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureState { get; set; }
    public int? CapturedAmount { get; set; }
    public int? Fee { get; set; }
    public int? Net { get; set; }
    public int RefundedAmount { get; set; }
    public string? FailureMessage { get; set; }
    public List<RefundView> Refunds { get; set; } = new();
}

public sealed class RefundView
{
    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public int Amount { get; set; }
    public int AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? FinixTransferId { get; set; }
    public string? ReversalId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
}

public sealed class RefundOperationResponse
{
    public int OrderId { get; set; }
    public int RefundId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public int RefundedAmount { get; set; }
    public RefundView Refund { get; set; } = new();
    public PaymentView Payment { get; set; } = new();
    public OrderView Order { get; set; } = new();
}

public sealed class SavedCardView
{
    public int PaymentMethodId { get; set; }
    public int Id { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class MyOrdersResponse
{
    public List<OrderView> Orders { get; set; } = new();
}

public sealed class SavedCardsResponse
{
    public List<SavedCardView> PaymentMethods { get; set; } = new();
    public List<SavedCardView> Cards { get; set; } = new();
}

public sealed class ReconciliationEntry
{
    public string Id { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? State { get; set; }
    public int? Amount { get; set; }
    public int? Fee { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int? OrderId { get; set; }
    public bool Matched { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class ReconciliationOrderEntry
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public int AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public int RefundedAmount { get; set; }
    public bool Matched { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
}

public sealed class ReconciliationSummary
{
    public int FinixTransactionCount { get; set; }
    public int MatchedFinixTransactionCount { get; set; }
    public int UnmatchedFinixTransactionCount { get; set; }
    public int EshopOrderCount { get; set; }
    public int MatchedEshopOrderCount { get; set; }
    public int UnmatchedEshopOrderCount { get; set; }
}

public sealed class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntry> Transactions { get; set; } = new();
    public List<ReconciliationEntry> FinixTransactions { get; set; } = new();
    public List<ReconciliationEntry> UnmatchedFinixTransactions { get; set; } = new();
    public List<ReconciliationOrderEntry> Orders { get; set; } = new();
    public List<ReconciliationOrderEntry> EshopOrders { get; set; } = new();
    public List<ReconciliationOrderEntry> UnmatchedEshopOrders { get; set; } = new();
    public ReconciliationSummary Summary { get; set; } = new();
}
