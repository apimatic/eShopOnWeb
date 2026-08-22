using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ICheckoutService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderCatalogItem> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default);

    Task<Order> PayAsync(
        string buyerId,
        int orderId,
        CardPayment? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<PaymentRefund> RefundAsync(
        string buyerId,
        int orderId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}

public sealed record OrderCatalogItem(int CatalogItemId, int Quantity);
