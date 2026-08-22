using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ICheckoutService
{
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, Address? shipToAddress);
    Task<Order> PayWithCardAsync(int orderId, string buyerId, CardInput card, CancellationToken cancellationToken);
    Task<Order> PayWithSavedCardAsync(int orderId, string buyerId, int paymentMethodId, CancellationToken cancellationToken);
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<OrderRefund> RefundAsync(int orderId, string buyerId, string idempotencyKey, decimal? amount, CancellationToken cancellationToken);
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId);
    Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId);
}

public sealed class CatalogOrderLine
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}
