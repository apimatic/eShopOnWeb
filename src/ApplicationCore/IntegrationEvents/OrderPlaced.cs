using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published after an order is created (<see cref="Interfaces.IOrderService.CreateOrderAsync"/>).
/// Lets an in-process handler record one pay-as-you-go usage unit for the buyer's subscription
/// (UC2's "one order placed -> one billable unit" demo hook), without coupling order placement to
/// the subscription/billing feature.
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
