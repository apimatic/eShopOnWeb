using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process whenever <see cref="Services.OrderService"/> creates an order. The
/// subscription feature's UC2 hook uses this to record one pay-as-you-go usage unit for the
/// buyer, if they have an active subscription.
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
