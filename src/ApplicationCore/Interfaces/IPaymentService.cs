using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    Task<Order> CreateOrderAsync(string buyerId,
        IReadOnlyList<CreateOrderItem> items,
        Address shippingAddress,
        CancellationToken cancellationToken = default);

    Task<Order> PayAsync(string buyerId,
        int orderId,
        PayOrderCommand command,
        CancellationToken cancellationToken = default);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<(Order Order, OrderRefund Refund)> RefundAsync(int orderId,
        long? amountCents,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<PaymentMethod> SavePaymentMethodAsync(string buyerId,
        CardDetails card,
        string? alias,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken = default);

    Task DeletePaymentMethodAsync(string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default);
}
