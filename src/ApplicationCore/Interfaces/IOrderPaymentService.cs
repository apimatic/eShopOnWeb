using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address? shipTo, CancellationToken cancellationToken = default);
    Task<Order> PayAsync(int orderId, string buyerId, CardPaymentDetails? card, int? paymentMethodId, CancellationToken cancellationToken = default);
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);
    Task<OrderRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}

public record OrderLineRequest(int CatalogItemId, int Quantity);
