using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A catalog item and how many of it the shopper is ordering.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>
/// Places an order directly from catalog item ids + quantities (no basket), reusing the existing
/// <see cref="Entities.OrderAggregate.Order"/> aggregate, and opens a payment awaiting payment.
/// </summary>
public interface IOrderPlacementService
{
    /// <summary>Creates the order + its awaiting-payment record and returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines, CancellationToken ct);
}
