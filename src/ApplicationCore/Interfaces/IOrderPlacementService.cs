using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>An item and quantity requested when placing an order directly from the catalog.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Places an order directly from catalog item ids and quantities, reusing the app's existing
/// Order / OrderItem model. Unit prices are taken from the catalog at order time (a snapshot), so
/// the caller never restates what things cost.
/// </summary>
public interface IOrderPlacementService
{
    Task<int> PlaceOrderAsync(
        string buyerId,
        IReadOnlyCollection<OrderLineRequest> items,
        Address shipToAddress,
        CancellationToken cancellationToken = default);
}
