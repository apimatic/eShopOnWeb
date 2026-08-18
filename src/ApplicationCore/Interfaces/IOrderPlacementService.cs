using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A single line of a placed order: how many of which catalog item.</summary>
public record OrderRequestItem(int CatalogItemId, int Quantity);

/// <summary>
/// Places an order directly from catalog item ids and quantities, building the app's existing
/// Order/OrderItem model (not a parallel one). The buyer's identity comes from the caller, not the request.
/// </summary>
public interface IOrderPlacementService
{
    Task<Result<Order>> PlaceOrderAsync(
        string buyerId,
        IReadOnlyCollection<OrderRequestItem> items,
        Address shipToAddress,
        CancellationToken ct = default);
}
