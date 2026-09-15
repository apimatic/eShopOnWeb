using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public interface ICommercePaymentService
{
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLineData> lines,
        Address shippingAddress, CancellationToken cancellationToken);
    Task<Order> PayAsync(int orderId, string buyerId, CardData? card, int? paymentMethodId,
        CancellationToken cancellationToken);
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<PaymentMethod> SavePaymentMethodAsync(string buyerId, CardData card,
        CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PaymentMethod>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken);
    Task DeletePaymentMethodAsync(int paymentMethodId, string buyerId,
        CancellationToken cancellationToken);
    Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
