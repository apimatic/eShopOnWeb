using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published after <c>OrderService.CreateOrderAsync</c> persists a new order. Drives the UC2 "one order
/// placed → one billable unit" demo hook (plan §8): a handler records one usage unit against the buyer's
/// active subscription, if they have one. Best-effort — a handler failure never rolls back the order.
/// </summary>
public class OrderPlaced : INotification
{
    public string BuyerId { get; }
    public int OrderId { get; }

    public OrderPlaced(string buyerId, int orderId)
    {
        BuyerId = buyerId;
        OrderId = orderId;
    }
}
