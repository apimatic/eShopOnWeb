using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record OrderLineInput(int CatalogItemId, int Quantity);

public sealed record ShippingAddressInput(
    string? Street,
    string? City,
    string? State,
    string? Country,
    string? ZipCode);

public sealed record CardInput(
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string? SecurityCode,
    string? Name,
    ShippingAddressInput? BillingAddress);

public sealed record CreateOrderInput(
    IReadOnlyList<OrderLineInput> Items,
    ShippingAddressInput? ShippingAddress);

public sealed record PayOrderInput(
    CardInput? Card,
    int? PaymentMethodId);

public sealed record RefundOrderInput(
    long? AmountCents,
    decimal? Amount,
    string IdempotencyKey);

public sealed record SaveCardInput(CardInput Card);

public sealed record OrderItemView(
    int CatalogItemId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

public sealed record RefundView(
    int RefundId,
    long AmountCents,
    string IdempotencyKey,
    string? FinixTransferId,
    string? FinixState,
    DateTimeOffset CreatedAt);

public sealed record PaymentView(
    int PaymentId,
    string Status,
    long AmountCents,
    string Currency,
    string? FinixAuthorizationId,
    string? FinixAuthorizationState,
    string? FinixCaptureTransferId,
    string? FinixCaptureState,
    string? FinixPaymentInstrumentId,
    bool UsedSavedCard,
    long? CapturedAmountCents,
    long? FinixFeeCents,
    long? NetAmountCents,
    long RefundableAmountCents,
    string? FailureCode,
    string? FailureMessage,
    DateTimeOffset? AuthorizedAt,
    DateTimeOffset? CapturedAt,
    DateTimeOffset? FulfilledAt,
    DateTimeOffset? CanceledAt,
    IReadOnlyList<RefundView> Refunds);

public sealed record OrderView(
    int OrderId,
    string BuyerId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Currency,
    IReadOnlyList<OrderItemView> Items,
    PaymentView? Payment);

public sealed record SavedCardView(
    int PaymentMethodId,
    string? Brand,
    string? LastFour,
    int? ExpirationMonth,
    int? ExpirationYear,
    DateTimeOffset CreatedAt);

public sealed record FinixTransactionView(
    string RecordType,
    string Id,
    long? AmountCents,
    string? Currency,
    string? State,
    string? Type,
    string? ParentTransferId,
    DateTimeOffset? CreatedAt,
    bool Matched,
    int? MatchedOrderId,
    int? MatchedRefundId);

public sealed record ReconciliationMismatch(
    string RecordType,
    string? FinixId,
    int? OrderId,
    int? RefundId,
    long? AmountCents,
    string? Status,
    string Reason);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int FinixTransactionCount,
    int EshopPaymentCount,
    IReadOnlyList<FinixTransactionView> FinixTransactions,
    IReadOnlyList<ReconciliationMismatch> UnmatchedFinixTransactions,
    IReadOnlyList<ReconciliationMismatch> UnmatchedEshopPayments);
