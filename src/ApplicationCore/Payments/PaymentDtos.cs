using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record BillingAddressDetails(
    string? Street,
    string? City,
    string? State,
    string? Country,
    string? ZipCode);

public sealed record CardDetails(
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string SecurityCode,
    string Name,
    BillingAddressDetails? BillingAddress = null);

public sealed record OrderItemRequest(int CatalogItemId, int Quantity);

public sealed record ShippingAddressDetails(
    string Street,
    string City,
    string State,
    string Country,
    string ZipCode);

public sealed record RefundView(
    int RefundId,
    string? FinixRefundId,
    long AmountCents,
    string? State,
    string IdempotencyKey,
    DateTimeOffset CreatedAt);

public sealed record PaymentView(
    string Status,
    string Currency,
    long AmountAuthorizedCents,
    long AmountCapturedCents,
    long AmountRefundedCents,
    long RefundableCents,
    decimal FeeCents,
    long NetCents,
    string? AuthorizationId,
    string? AuthorizationState,
    bool AuthorizationVoided,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureTransferId,
    string? CaptureState,
    string? FailureCode,
    string? FailureMessage,
    IReadOnlyList<RefundView> Refunds);

public sealed record OrderPlacementResult(
    int OrderId,
    decimal Total,
    string Currency,
    PaymentView Payment);

public sealed record PaymentOperationResult(
    int OrderId,
    PaymentView Payment);

public sealed record RefundOperationResult(
    int RefundId,
    string? FinixRefundId,
    int OrderId,
    long AmountCents,
    string? State,
    PaymentView Payment);

public sealed record OrderSummary(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Currency,
    PaymentView Payment);

public sealed record SavedCardResult(
    int PaymentMethodId,
    string? Brand,
    string? Last4,
    int? ExpirationMonth,
    int? ExpirationYear,
    string? CardType,
    DateTimeOffset CreatedAt);

public sealed record ReconciliationFinixTransaction(
    string Id,
    string Kind,
    string? State,
    long? AmountCents,
    string? Type,
    string? ParentId,
    DateTimeOffset? CreatedAt,
    int? MatchedOrderId,
    string? MatchReason,
    string? FailureCode,
    string? FailureMessage);

public sealed record ReconciliationEshopOrder(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Currency,
    string Status,
    string? AuthorizationId,
    string? CaptureTransferId,
    IReadOnlyList<string> MatchedFinixTransactionIds);

public sealed record ReconciliationDiscrepancy(
    string Type,
    string Message,
    int? OrderId,
    string? FinixTransactionId);

public sealed record ReconciliationResult(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationFinixTransaction> FinixTransactions,
    IReadOnlyList<ReconciliationEshopOrder> EshopOrders,
    IReadOnlyList<ReconciliationDiscrepancy> Discrepancies);
