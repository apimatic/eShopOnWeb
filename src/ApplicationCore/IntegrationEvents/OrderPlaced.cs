using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

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
