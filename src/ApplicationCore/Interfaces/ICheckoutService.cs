using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ICheckoutService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address? shipTo,
        CancellationToken cancellationToken = default);

    Task<Order> PayAsync(
        string buyerId,
        int orderId,
        int? paymentMethodId,
        CardDetails? card,
        CancellationToken cancellationToken = default);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<(Order Order, OrderRefund Refund)> RefundAsync(
        string buyerId,
        int orderId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
