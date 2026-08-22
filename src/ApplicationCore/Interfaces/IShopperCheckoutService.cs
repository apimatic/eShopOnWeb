using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IShopperCheckoutService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<(int CatalogItemId, int Quantity)> items,
        Address shipToAddress,
        CancellationToken cancellationToken = default);
}
