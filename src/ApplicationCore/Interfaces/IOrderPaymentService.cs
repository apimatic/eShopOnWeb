using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payment;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<Order> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<(int CatalogItemId, int Quantity)> items,
        Address? shipTo,
        CancellationToken cancellationToken);

    Task<Order> PayAsync(
        string buyerId,
        int orderId,
        CardPaymentInput? card,
        int? paymentMethodId,
        CancellationToken cancellationToken);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);

    Task<Order> RefundAsync(
        string buyerId,
        int orderId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<Order> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken);
}
