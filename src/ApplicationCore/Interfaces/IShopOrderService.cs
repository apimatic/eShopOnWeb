using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places, dispatches and cancels orders through the app's existing order/order-item model, firing the
/// matching shopper notification for each transition. Each transition is its own call; a failed
/// notification never fails the transition.
/// </summary>
public interface IShopOrderService
{
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineItem> lines,
        Address shipToAddress, CancellationToken cancellationToken = default);

    Task<OrderOperationResult> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderOperationResult> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);
}

/// <summary>A requested line of an order: a catalog item and how many.</summary>
public class OrderLineItem
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public class PlaceOrderResult
{
    public Order? Order { get; init; }

    /// <summary>A validation message when the order could not be placed (bad request); null on success.</summary>
    public string? Error { get; init; }
}

public enum OrderOperationStatus
{
    Success,
    NotFound,
    Invalid
}

public class OrderOperationResult
{
    public OrderOperationStatus Status { get; init; }
    public Order? Order { get; init; }
    public string? Error { get; init; }

    public static OrderOperationResult Ok(Order order) => new() { Status = OrderOperationStatus.Success, Order = order };
    public static OrderOperationResult NotFound() => new() { Status = OrderOperationStatus.NotFound };
    public static OrderOperationResult InvalidState(string error) => new() { Status = OrderOperationStatus.Invalid, Error = error };
}
