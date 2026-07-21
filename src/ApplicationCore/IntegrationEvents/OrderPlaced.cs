using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after an order is successfully created. Best-effort (§2.5) - a handler
/// failure never rolls back or blocks order creation, which has already completed by the time this
/// is published. Used to drive the UC2 "one order placed - one billable API-call unit" demo hook.
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
