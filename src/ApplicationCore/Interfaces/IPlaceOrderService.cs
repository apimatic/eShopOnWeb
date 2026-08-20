using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPlaceOrderService
{
    Task<Result<Order>> PlaceAsync(string buyerId, IReadOnlyList<OrderLineRequest> items);
}

public record OrderLineRequest(int CatalogItemId, int Quantity);
