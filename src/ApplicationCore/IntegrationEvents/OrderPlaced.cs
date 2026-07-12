using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published by <see cref="Services.OrderService"/> after an order is created. Powers UC2's
/// "one order placed → one billable api-call unit" hook (§8 decision) — best-effort, in-process.
/// </summary>
public class OrderPlaced : INotification
{
    public string BuyerId { get; }
    public int OrderId { get; }

    public OrderPlaced(string buyerId, int orderId)
    {
        BuyerId = buyerId;
        OrderId = orderId;
    }
}
