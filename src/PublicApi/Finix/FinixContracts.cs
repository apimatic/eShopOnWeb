using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Finix;

public sealed record OrderLine(int CatalogItemId, int Quantity);

public sealed record CardDetails(
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string Name,
    string? SecurityCode,
    AddressDetails? Address);

public sealed record AddressDetails(
    string Line1,
    string? Line2,
    string City,
    string Region,
    string PostalCode,
    string Country);

public sealed record SavedCardView(
    string PaymentMethodId,
    string? Last4,
    string? Brand,
    int? ExpirationMonth,
    int? ExpirationYear,
    bool Enabled,
    DateTimeOffset CreatedAt);

public sealed record RefundView(
    int Id,
    string RefundId,
    long AmountCents,
    string? State,
    DateTimeOffset CreatedAt);

public sealed record OrderPaymentView(
    int OrderId,
    string Status,
    string Currency,
    long AmountCents,
    string? AuthorizationId,
    string? AuthorizationState,
    long? AuthorizedAmountCents,
    bool? IsVoid,
    string? VoidState,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureTransferId,
    long? CapturedAmountCents,
    long? FeeCents,
    long? NetCents,
    string? CaptureState,
    string? FailureCode,
    string? FailureMessage,
    IReadOnlyList<RefundView> Refunds);

public sealed record MyOrderItemView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public sealed record MyOrderView(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    string Currency,
    IReadOnlyList<MyOrderItemView> Items,
    OrderPaymentView? Payment);

public sealed record ReconciliationTransferView(
    string TransferId,
    string? Type,
    string? State,
    long? Amount,
    long? Fee,
    DateTimeOffset? CreatedAt,
    int? MatchedOrderId,
    string? MatchKind,
    bool Matched,
    string? Mismatch);

public sealed record ReconciliationAppOnlyView(
    int OrderId,
    string Kind,
    string? FinixId,
    long AmountCents,
    string Status);

public sealed record ReconciliationResult(
    IReadOnlyList<ReconciliationTransferView> FinixTransfers,
    IReadOnlyList<ReconciliationAppOnlyView> AppOnly,
    bool HasMore,
    DateTimeOffset From,
    DateTimeOffset To);

public sealed record FinixPage<T>(IReadOnlyList<T> Items, bool HasMore);
