using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services.Payments;

public sealed record NewOrderItem(int CatalogItemId, int Quantity);

public sealed record PayOrderCommand(int OrderId, string BuyerId, FinixCardDetails? Card, int? PaymentMethodId);

public sealed record RefundOrderCommand(int OrderId, string BuyerId, decimal? Amount, string IdempotencyKey);

public sealed record OrderItemView(
    int CatalogItemId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

public sealed record RefundView(
    int RefundId,
    long AmountCents,
    decimal Amount,
    string Status,
    string IdempotencyKey,
    string? FinixTransferId,
    string? FailureCode,
    string? FailureMessage,
    DateTimeOffset CreatedAt);

public sealed record PaymentView(
    string Status,
    string Currency,
    long AmountCents,
    decimal Amount,
    string? AuthorizationId,
    string? AuthorizationState,
    DateTimeOffset? AuthorizationExpiresAt,
    bool AuthorizationIsVoid,
    string? AuthorizationVoidState,
    string? CaptureTransferId,
    string? CaptureState,
    long? CapturedAmountCents,
    decimal? CapturedAmount,
    long? FeeCents,
    decimal? Fee,
    long? NetCents,
    decimal? Net,
    long RefundedAmountCents,
    decimal RefundedAmount,
    long RemainingRefundableCents,
    decimal RemainingRefundable,
    string? FinixPaymentInstrumentId,
    int? PaymentMethodId,
    string? CardBrand,
    string? CardLast4,
    string? FailureCode,
    string? FailureMessage,
    IReadOnlyList<RefundView> Refunds);

public sealed record OrderView(
    int OrderId,
    DateTimeOffset OrderDate,
    string BuyerId,
    decimal Total,
    long TotalCents,
    string Currency,
    string PaymentStatus,
    string FulfillmentStatus,
    IReadOnlyList<OrderItemView> Items,
    PaymentView? Payment);

public sealed record PayResult(OrderView Order, bool IdempotentReplay);

public sealed record RefundResult(
    int RefundId,
    int OrderId,
    long AmountCents,
    decimal Amount,
    string Currency,
    string Status,
    string IdempotencyKey,
    string? FinixTransferId,
    string? FailureCode,
    string? FailureMessage,
    bool IdempotentReplay);

public sealed record SavedCardView(
    int PaymentMethodId,
    string Brand,
    string Last4,
    int ExpirationMonth,
    int ExpirationYear,
    string? CardType,
    string? Name,
    DateTimeOffset CreatedAt);

public sealed record ReconciliationRecord(
    string Kind,
    string FinixId,
    string? Type,
    string? State,
    long AmountCents,
    decimal Amount,
    string? Currency,
    DateTimeOffset? CreatedAt,
    string? ParentTransferId,
    string? Source,
    string? FailureCode,
    string? FailureMessage,
    int? MatchedOrderId,
    string MatchStatus);

public sealed record ReconciliationOrder(
    int OrderId,
    string BuyerId,
    DateTimeOffset OrderDate,
    decimal Total,
    long TotalCents,
    string Currency,
    string PaymentStatus,
    string FulfillmentStatus,
    string? AuthorizationId,
    string? CaptureTransferId,
    IReadOnlyList<string> RefundTransferIds,
    string MatchStatus);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string Currency,
    IReadOnlyList<ReconciliationRecord> FinixTransactions,
    IReadOnlyList<ReconciliationOrder> EshopOrders,
    int MatchedCount,
    int FinixOnlyCount,
    int EshopOnlyCount);

public interface IOrderPaymentService
{
    Task<OrderView> CreateOrderAsync(string buyerId,
        IReadOnlyCollection<NewOrderItem> items,
        string? street,
        string? city,
        string? state,
        string? country,
        string? zipCode,
        CancellationToken cancellationToken);

    Task<PayResult> PayAsync(PayOrderCommand command, CancellationToken cancellationToken);

    Task<OrderView> FulfilAsync(int orderId, CancellationToken cancellationToken);

    Task<OrderView> CancelAsync(int orderId, CancellationToken cancellationToken);

    Task<RefundResult> RefundAsync(RefundOrderCommand command, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    Task<SavedCardView> SavePaymentMethodAsync(string buyerId, FinixCardDetails card, CancellationToken cancellationToken);

    Task<IReadOnlyList<SavedCardView>> GetPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken);

    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
}
