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
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines, Address address, CancellationToken cancellationToken);
    Task<Order> PayAsync(string buyerId, int orderId, CardDetails? card, int? paymentMethodId, CancellationToken cancellationToken);
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<PaymentMethod> SavePaymentMethodAsync(string buyerId, CardDetails card, CancellationToken cancellationToken);
    Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken);
    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
