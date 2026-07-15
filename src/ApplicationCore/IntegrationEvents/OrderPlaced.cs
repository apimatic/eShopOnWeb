using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after <c>OrderService.CreateOrderAsync</c> persists an order. Decided in
/// plan.md §8: the UC2 demo hook is "one order placed -&gt; one billable usage unit", handled by an
/// in-process subscriber rather than coupling <c>OrderService</c> directly to billing.
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
