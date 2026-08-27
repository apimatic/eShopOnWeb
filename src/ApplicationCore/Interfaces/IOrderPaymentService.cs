using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record OrderItemRequest(int CatalogItemId, int Quantity);

public interface IOrderPaymentService
{
    /// <summary>Place an order from catalog items; it starts awaiting payment.</summary>
    Task<Order> CreateOrderAsync(string buyerId, Address shipToAddress,
        IReadOnlyList<OrderItemRequest> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorize the order total with either one-off card details or one of the
    /// shopper's saved cards. Repeating the call returns the existing authorization.
    /// </summary>
    Task<OrderPayment> PayOrderAsync(string buyerId, int orderId, CardDetails? card,
        int? savedCardId, CancellationToken cancellationToken = default);

    /// <summary>Fulfil the order: capture the authorized funds, renewing a stale authorization first.</summary>
    Task<OrderPayment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancel before fulfilment: release the shopper's held funds.</summary>
    Task<OrderPayment?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refund the captured payment in full (amount null) or in part.
    /// <paramref name="buyerId"/> is null for operators; when set, ownership is enforced.
    /// </summary>
    Task<PaymentRefund> RefundOrderAsync(int orderId, string? buyerId, string idempotencyKey,
        decimal? amount, string? noteToPayer, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderPayment>> GetPaymentsForOrdersAsync(IReadOnlyCollection<int> orderIds,
        CancellationToken cancellationToken = default);

    Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
