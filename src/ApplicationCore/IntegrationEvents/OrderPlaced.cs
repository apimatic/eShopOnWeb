using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process, best-effort, after an order is successfully created via
/// OrderService.CreateOrderAsync. Demo hook for UC2: one order placed raises this notification and
/// a Web-side handler records one usage unit against the buyer's active subscription, if any.
/// </summary>
public class OrderPlaced : INotification
{
    public OrderPlaced(string buyerId, int orderId)
    {
        BuyerId = buyerId;
        OrderId = orderId;
    }

    public string BuyerId { get; }
    public int OrderId { get; }
}
