using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Places an order directly from catalog item ids and quantities (bypassing the basket), reusing the
/// app's existing <see cref="Order"/>/<see cref="OrderItem"/> model. This is the API's own entry point
/// into the order flow so invoicing can be driven end-to-end through PublicApi alone.
/// </summary>
public interface IOrderPlacementService
{
    Task<Order> PlaceOrderAsync(string buyerId, Address shipToAddress,
        IReadOnlyCollection<OrderLineRequest> items, CancellationToken cancellationToken);
}
