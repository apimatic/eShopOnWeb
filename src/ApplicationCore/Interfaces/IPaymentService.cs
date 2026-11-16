using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    Task<Order> CreateOrderAsync(Payments.CreateOrderCommand command, CancellationToken cancellationToken = default);

    Task<Payment> PayOrderAsync(string buyerId, int orderId, Payments.PayOrderCommand command,
        CancellationToken cancellationToken = default);

    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Payment> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Refund> RefundOrderAsync(string buyerId, int orderId, long? amountCents, string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Payments.ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<SavedPaymentMethod> SavePaymentMethodAsync(string buyerId, Payments.CardDetails card,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken = default);

    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default);
}
