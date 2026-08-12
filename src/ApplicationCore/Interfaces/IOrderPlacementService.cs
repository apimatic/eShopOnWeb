using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>Result of placing an order.</summary>
public record OrderPlacementResult(bool Succeeded, int OrderId, string? Error);

/// <summary>
/// Places an order for a shopper from catalog items, reusing the app's existing order/order-item
/// model, and tells the shopper their order was placed. A failure to message never fails the order.
/// </summary>
public interface IOrderPlacementService
{
    Task<OrderPlacementResult> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, CancellationToken cancellationToken = default);
}
