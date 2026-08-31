using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places an order straight from catalog items, reusing the app's existing order/order-item model.
/// Used by the API surface so the invoicing flow is drivable end-to-end through PublicApi alone.
/// </summary>
public interface IOrderPlacementService
{
    Task<OrderPlacementResult> PlaceOrderAsync(
        string buyerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken cancellationToken = default);
}
