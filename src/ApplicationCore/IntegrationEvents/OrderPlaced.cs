using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after an order is created (<c>OrderService.CreateOrderAsync</c>). Consumed by
/// <c>OrderPlacedUsageHandler</c> to record one pay-as-you-go usage unit against the buyer's active
/// subscription, if any (UC2's "one order placed -> one billable unit" demo hook - plan.md §8).
/// </summary>
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
