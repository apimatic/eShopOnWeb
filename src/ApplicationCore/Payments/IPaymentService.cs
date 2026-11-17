using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record OrderDetails(Order Order, OrderPayment? Payment);

public sealed record ReconciliationEntry(
    string FinixTransferId,
    string Type,
    string State,
    int AmountCents,
    int FeeCents,
    string Currency,
    DateTimeOffset CreatedAt,
    string? ParentTransferId,
    int? OrderId,
    int? PaymentId,
    string MatchStatus,
    string? Detail);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries);

public interface IPaymentService
{
    Task<OrderDetails> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<(int CatalogItemId, int Quantity)> items,
        Address shipToAddress,
        CancellationToken cancellationToken = default);

    Task<OrderDetails> PayOrderAsync(
        string buyerId,
        int orderId,
        CardPaymentInput? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    Task<OrderDetails> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderDetails> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<PaymentRefund> RefundOrderAsync(
        int orderId,
        int? amountCents,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderDetails>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<SavedCard> SaveCardAsync(
        string buyerId,
        CardPaymentInput card,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCard>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
