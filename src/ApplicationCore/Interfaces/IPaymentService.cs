using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    Task<Order> CreateOrderAsync(string buyerId,
        CreateOrderInput input,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default);

    Task<OrderPayment> PayOrderAsync(string buyerId,
        int orderId,
        CardDetails? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    Task<OrderPayment> FulfilOrderAsync(int orderId,
        CancellationToken cancellationToken = default);

    Task<OrderPayment> CancelOrderAsync(int orderId,
        CancellationToken cancellationToken = default);

    Task<RefundResult> RefundOrderAsync(int orderId,
        long amountCents,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SavedCard> SaveCardAsync(string buyerId,
        CardDetails card,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCard>> GetSavedCardsAsync(string buyerId,
        CancellationToken cancellationToken = default);

    Task DeleteSavedCardAsync(string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
