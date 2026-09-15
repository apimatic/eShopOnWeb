using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderCheckoutService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderItem> items,
        Address shippingAddress,
        CancellationToken cancellationToken);
}

public sealed record PlaceOrderItem(int CatalogItemId, int Quantity);
