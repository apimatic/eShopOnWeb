using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderCheckoutService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address shippingAddress,
        CancellationToken cancellationToken);

    Task<Order> PayAsync(
        string buyerId,
        int orderId,
        CardPaymentSource? card,
        int? paymentMethodId,
        CancellationToken cancellationToken);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);

    Task<OrderRefund> RefundAsync(
        string buyerId,
        int orderId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken);
}

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
