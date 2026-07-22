using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after an order is created. The subscription module listens for this to
/// meter one billable unit per order (plan.md §8, UC2's automatic trigger); nothing else in the
/// existing checkout flow changes.
/// </summary>
public class OrderPlaced : INotification
{
    public OrderPlaced(int orderId, string buyerId)
    {
        OrderId = orderId;
        BuyerId = buyerId;
    }

    public int OrderId { get; }

    /// <summary>The eShopOnWeb username the order belongs to — the same reference subscriptions use.</summary>
    public string BuyerId { get; }
}
