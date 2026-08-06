using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested line on a new order: a catalog item and how many of it.</summary>
public sealed record OrderLineItem(int CatalogItemId, int Quantity);

/// <summary>
/// Places an order directly from catalog items (rather than from a persisted basket), reusing the
/// app's existing <see cref="Order"/>/<see cref="OrderItem"/> model. Prices are taken from the
/// catalog at placement time; the order starts <see cref="OrderPaymentStatus.AwaitingPayment"/>.
/// </summary>
public interface IOrderPlacementService
{
    Task<Result<Order>> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineItem> lines,
        Address shipToAddress, CancellationToken cancellationToken = default);
}
