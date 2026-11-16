using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class AddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public sealed class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class CreateOrderRequest
{
    public List<OrderItemRequest>? Items { get; set; }
    public AddressRequest? ShippingAddress { get; set; }
}

public sealed class CardRequest
{
    public string? Number { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public AddressRequest? Address { get; set; }
}

public sealed class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
    public string? IdempotencyKey { get; set; }
}

public sealed class RefundOrderRequest
{
    public int AmountCents { get; set; }
    public string? IdempotencyKey { get; set; }
}

public sealed record OrderItemSummary(int CatalogItemId, string ProductName, decimal UnitPrice, int Units, decimal LineTotal);

public sealed record RefundSummary(
    int RefundId,
    int AmountCents,
    string State,
    string IdempotencyKey,
    string? FinixTransferId,
    DateTimeOffset CreatedAt,
    string? FailureCode,
    string? FailureMessage);

public sealed record PaymentSummary(
    int OrderId,
    string OrderStatus,
    string PaymentStatus,
    int AmountCents,
    string Currency,
    string? AuthorizationId,
    string? AuthorizationState,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureTransferId,
    string? CaptureState,
    int? CapturedAmountCents,
    int? FeeAmountCents,
    int? NetAmountCents,
    bool FeesPending,
    int RefundedAmountCents,
    int RefundableAmountCents,
    string? FailureCode,
    string? FailureMessage,
    IReadOnlyList<RefundSummary> Refunds);

public sealed record OrderSummary(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    int TotalCents,
    string Currency,
    IReadOnlyList<OrderItemSummary> Items,
    PaymentSummary Payment);

public sealed record SavedCardSummary(
    int PaymentMethodId,
    string Brand,
    string LastFour,
    int? ExpirationMonth,
    int? ExpirationYear,
    string? CardType,
    string? Name,
    DateTimeOffset CreatedAt);

public sealed record ReconciliationTransaction(
    string Id,
    string? Type,
    string? Subtype,
    string? State,
    int AmountCents,
    string? Currency,
    DateTimeOffset? CreatedAt,
    string? ParentTransferId,
    string? Source,
    string? TraceId,
    string? FailureCode,
    string? FailureMessage,
    int? MatchedOrderId,
    string MatchStatus);

public sealed record ReconciliationEshopPayment(
    int OrderId,
    string PaymentStatus,
    string? AuthorizationId,
    string? CaptureTransferId,
    int AmountCents,
    int? CapturedAmountCents,
    int RefundedAmountCents,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CapturedAt,
    IReadOnlyList<RefundSummary> Refunds,
    string MatchStatus);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationTransaction> FinixTransactions,
    IReadOnlyList<ReconciliationEshopPayment> EshopPayments,
    IReadOnlyList<string> UnmatchedFinixTransactionIds,
    IReadOnlyList<int> UnmatchedEshopOrderIds,
    int MatchedCount,
    int FinixTransactionCount,
    int EshopPaymentCount);
