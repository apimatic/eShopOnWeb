using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class CatalogLine
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public interface ICheckoutService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogLine> lines, Address? shipTo, CancellationToken ct);
}
