using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ICheckoutPaymentService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLine> lines,
        Address shipTo,
        string currency,
        CancellationToken cancellationToken);

    Task<Order> PayAsync(
        int orderId,
        string buyerId,
        PayPalCardInput? card,
        string? paymentMethodId,
        CancellationToken cancellationToken);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);

    Task<OrderRefund> RefundAsync(
        int orderId,
        string buyerId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken);
}

public sealed record OrderLine(int CatalogItemId, int Quantity);
