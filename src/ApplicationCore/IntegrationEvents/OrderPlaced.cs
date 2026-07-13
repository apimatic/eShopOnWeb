using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process (best-effort) after <c>OrderService.CreateOrderAsync</c> persists an order.
/// The demo's "automatic" pay-as-you-go hook: one order placed records one api-call usage unit
/// against the buyer's active subscription, if they have one.
/// </summary>
public class OrderPlaced : INotification
{
    public OrderPlaced(string buyerId)
    {
        BuyerId = buyerId;
    }

    public string BuyerId { get; }
}
