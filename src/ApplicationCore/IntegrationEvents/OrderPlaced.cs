using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

// Raised after an order is created so in-process handlers can react (e.g. record one
// pay-as-you-go usage unit against the buyer's active subscription, per plan §8/UC2).
public class OrderPlaced : INotification
{
    public OrderPlaced(int orderId, string buyerId)
    {
        OrderId = orderId;
        BuyerId = buyerId;
    }

    public int OrderId { get; }
    public string BuyerId { get; }
}
