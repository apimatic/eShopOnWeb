using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ICatalogOrderService
{
    Task<Order> PlaceAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> lines,
        Address? shipTo,
        CancellationToken cancellationToken);
}
