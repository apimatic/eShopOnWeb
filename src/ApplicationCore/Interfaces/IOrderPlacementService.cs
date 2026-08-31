using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Places an order directly from catalog item ids and quantities for a given buyer, reusing the
/// app's existing <see cref="Order"/> / <see cref="OrderItem"/> model. Prices and product details
/// come from the catalog, not from the caller, and the created order is returned so the caller can
/// go on to raise a bill against it.
/// </summary>
public interface IOrderPlacementService
{
    Task<OperationResult<Order>> PlaceOrderAsync(
        string buyerId,
        IReadOnlyCollection<OrderLineRequest> lines,
        Address shipToAddress,
        CancellationToken cancellationToken = default);
}
