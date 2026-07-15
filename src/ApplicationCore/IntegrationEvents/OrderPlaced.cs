using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process, best-effort, after <see cref="Services.OrderService.CreateOrderAsync"/>
/// persists a new order. Decided in the integration plan (§8, UC2 trigger): one order placed
/// records one <c>api-call</c> usage unit against the buyer's active subscription, if any.
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
