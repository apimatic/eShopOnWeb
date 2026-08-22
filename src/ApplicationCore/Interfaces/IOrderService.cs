using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    Task<Order> CreateOrderFromCatalogItemsAsync(
        string buyerId,
        IReadOnlyCollection<CatalogQuantity> items,
        Address? shippingAddress = null,
        CancellationToken cancellationToken = default);

    Task<OrderMutationResult?> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderMutationResult?> CancelAsync(int orderId, CancellationToken cancellationToken = default);
}

public readonly record struct CatalogQuantity(int CatalogItemId, int Quantity);

public sealed class OrderMutationResult
{
    public OrderMutationResult(Order order, bool statusChanged)
    {
        Order = order;
        StatusChanged = statusChanged;
    }

    public Order Order { get; }
    public bool StatusChanged { get; }
}
