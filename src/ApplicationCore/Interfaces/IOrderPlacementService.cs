using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places an order for a shopper from catalog item ids and quantities, reusing the app's existing
/// order / order-item model.
/// </summary>
public interface IOrderPlacementService
{
    Task<Result<Order>> PlaceOrderAsync(
        string buyerId,
        IReadOnlyCollection<OrderLineRequest> lines,
        Address? shipToAddress,
        CancellationToken cancellationToken = default);
}
