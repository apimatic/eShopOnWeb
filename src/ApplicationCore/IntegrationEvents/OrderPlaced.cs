using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

// Published in-process, best-effort, after an order is created (OrderService.CreateOrderAsync).
// Drives UC2's "one order placed -> one billable usage unit" demo hook.
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
