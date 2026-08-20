using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogOrderLine(int CatalogItemId, int Quantity);

public record PlacedOrderResult(Order Order, OrderNotification? Notification);

public interface IShopperOrderService
{
    Task<PlacedOrderResult> PlaceAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> lines,
        Address shippingAddress,
        CancellationToken cancellationToken);
}
