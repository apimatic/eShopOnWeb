using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published after an order is created (plan.md §8 UC2 trigger: "one order placed → one billable unit").
/// A subscription-side handler subscribes to record one usage unit for the buyer's active subscription, if
/// any — the hook that demos "automatic" pay-as-you-go usage without coupling <c>OrderService</c> to
/// subscriptions directly.
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
