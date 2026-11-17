using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record CreateOrderItem(int CatalogItemId, int Quantity);

public sealed record CreateOrderCommand(
    IReadOnlyList<CreateOrderItem> Items,
    string? Street,
    string? City,
    string? State,
    string? Country,
    string? ZipCode);

public sealed record PayOrderCommand(
    int? SavedPaymentMethodId,
    CardDetails? Card,
    string? SecurityCode);

public sealed record SaveCardCommand(CardDetails Card);

public sealed record RefundOrderCommand(decimal? Amount, string IdempotencyKey);

public sealed record OrderItemView(
    int CatalogItemId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

public sealed record RefundView(
    int RefundId,
    string? FinixRefundId,
    decimal Amount,
    long AmountMinor,
    string Status,
    string IdempotencyKey,
    string? FailureCode,
    string? FailureMessage,
    DateTimeOffset CreatedAt);

public sealed record PaymentView(
    string Status,
    long AmountMinor,
    decimal Amount,
    string Currency,
    string? AuthorizationId,
    string? AuthorizationState,
    string? VoidState,
    string? CaptureId,
    string? CaptureState,
    long? CapturedAmountMinor,
    decimal? CapturedAmount,
    decimal? FeeMinor,
    decimal? Fee,
    decimal? NetMinor,
    decimal? Net,
    long RefundedAmountMinor,
    decimal RefundedAmount,
    string? FailureCode,
    string? FailureMessage,
    IReadOnlyList<RefundView> Refunds);

public sealed record OrderView(
    int OrderId,
    string Status,
    decimal Total,
    long TotalMinor,
    string Currency,
    DateTimeOffset OrderDate,
    IReadOnlyList<OrderItemView> Items,
    PaymentView? Payment);

public sealed record SavedPaymentMethodView(
    int PaymentMethodId,
    string Brand,
    string Last4,
    int ExpirationMonth,
    int ExpirationYear,
    string? Name,
    DateTimeOffset CreatedAt);

public sealed record ReconciliationItem(
    string? FinixTransactionId,
    int? OrderId,
    int? RefundId,
    string Kind,
    long AmountMinor,
    decimal Amount,
    string Currency,
    string State,
    DateTimeOffset? CreatedAt,
    string? OrderReference,
    string? Note);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationItem> Matched,
    IReadOnlyList<ReconciliationItem> FinixOnly,
    IReadOnlyList<ReconciliationItem> EshopOnly,
    int FinixTransactionCount,
    int EshopTransactionCount);
