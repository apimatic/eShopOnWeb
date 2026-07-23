using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a one-time order was checked out. The subscription feature listens for this to
/// bill one metered unit per order placed (UC2).
/// </summary>
public class OrderPlaced : INotification
{
    public OrderPlaced(string buyerId, int orderId)
    {
        BuyerId = buyerId;
        OrderId = orderId;
    }

    /// <summary>The eShopOnWeb user reference that placed the order.</summary>
    public string BuyerId { get; }

    public int OrderId { get; }
}
