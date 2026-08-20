using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public record PlaceOrderAddress(string Street, string City, string State, string Country, string ZipCode);

public interface ICheckoutOrderService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items, PlaceOrderAddress? shipTo);
}
