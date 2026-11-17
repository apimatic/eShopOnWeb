using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Finix;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public enum PaymentOperationError
{
    Validation,
    NotFound,
    Forbidden,
    Conflict,
    Upstream
}

public class PaymentOperationException : Exception
{
    public PaymentOperationException(PaymentOperationError error, string message)
        : base(message)
    {
        Error = error;
    }

    public PaymentOperationError Error { get; }
}

public record NewOrderItem(int CatalogItemId, int Quantity);

public record PayOrderCommand(CardDetails? Card, int? PaymentMethodId);

public record RefundView(
    int RefundId,
    decimal Amount,
    string State,
    string FinixTransferId,
    DateTimeOffset CreatedAt);

public record PaymentView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Currency,
    string PaymentStatus,
    string FulfilmentStatus,
    string? AuthorizationId,
    string? AuthorizationState,
    string? CaptureTransferId,
    string? CaptureTransferState,
    decimal? AuthorizedAmount,
    decimal? CapturedAmount,
    decimal? Fee,
    decimal? Net,
    decimal? RefundedAmount,
    string? FailureMessage,
    IReadOnlyList<RefundView> Refunds);

public record SavedCardView(
    int PaymentMethodId,
    string Brand,
    string LastFour,
    int ExpirationMonth,
    int ExpirationYear,
    string? CardType,
    DateTimeOffset CreatedAt);

public record ReconciliationFinixTransaction(
    string Kind,
    string Id,
    string? Type,
    string? State,
    decimal Amount,
    string? Currency,
    string? ParentId,
    DateTimeOffset? CreatedAt,
    int? MatchedOrderId);

public record ReconciliationOrder(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Currency,
    string PaymentStatus,
    string FulfilmentStatus,
    string? AuthorizationId,
    string? CaptureTransferId,
    IReadOnlyList<string> RefundTransferIds);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationFinixTransaction> FinixTransactions,
    IReadOnlyList<ReconciliationOrder> EshopOrders,
    IReadOnlyList<string> UnmatchedFinixTransactionIds,
    IReadOnlyList<int> UnmatchedEshopOrderIds);
