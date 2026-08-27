using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPlaceOrderService
{
    Task<Order> PlaceAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, CancellationToken cancellationToken);
}

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
