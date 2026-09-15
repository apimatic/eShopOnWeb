using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ICatalogCheckoutService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderItem> items, Address? shippingAddress = null, CancellationToken cancellationToken = default);
}

public record CatalogOrderItem(int CatalogItemId, int Quantity);
