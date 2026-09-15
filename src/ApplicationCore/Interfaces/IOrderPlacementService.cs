using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places an order from catalog items (reusing the existing Order/OrderItem model) and starts it in a
/// state awaiting payment. Prices come from the catalog; the currency from configuration.
/// </summary>
public interface IOrderPlacementService
{
    Task<PlacedOrder> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines,
        ShippingAddress? shipToAddress, CancellationToken cancellationToken = default);
}

public record OrderLine(int CatalogItemId, int Quantity);

public record ShippingAddress(string Street, string City, string State, string Country, string ZipCode);

public record PlacedOrder(Order Order, Payment Payment);
