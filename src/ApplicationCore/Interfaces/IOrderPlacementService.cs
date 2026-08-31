using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPlacementService
{
    /// <summary>Places an order from catalog items and notifies the shopper. Notification
    /// failures never fail the placement.</summary>
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items,
        Address? shipToAddress, CancellationToken ct = default);
}

public record OrderItemRequest(int CatalogItemId, int Quantity);

public record PlaceOrderResult(Order? Order, string? Error)
{
    public bool Success => Order != null;
}
