using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record OrderItemRequest(int CatalogItemId, int Quantity);

public sealed record ReconciliationEntry(
    GatewayTransaction Transaction,
    int? MatchedOrderId,
    int? MatchedPaymentId);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Transactions,
    IReadOnlyList<ReconciliationEntry> UnmatchedTransactions,
    IReadOnlyList<int> OrdersWithoutPayPalTransaction);

/// <summary>
/// Orchestrates the order payment lifecycle: authorize at checkout, capture at
/// fulfilment, void on cancel, refund after fulfilment, plus saved cards and
/// reconciliation against the provider's own transaction record.
/// </summary>
public interface IOrderPaymentService
{
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address? shipToAddress,
        CancellationToken ct = default);

    /// <summary>Returns null when the order does not exist or belongs to another shopper.</summary>
    Task<Payment?> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken ct = default);

    /// <summary>Operator action. Returns null when the order does not exist.</summary>
    Task<Payment?> FulfilOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action. Returns null when the order does not exist; the payment is null when none was ever taken.</summary>
    Task<(Order Order, Payment? Payment)?> CancelOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Returns null when the order does not exist or belongs to another shopper.</summary>
    Task<PaymentRefund?> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        string? noteToPayer, CancellationToken ct = default);

    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken ct = default);

    Task<IReadOnlyList<Payment>> GetPaymentsForOrdersAsync(IReadOnlyCollection<int> orderIds,
        CancellationToken ct = default);

    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListCardsAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Returns false when the card does not exist or belongs to another shopper.</summary>
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);

    Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);
}
