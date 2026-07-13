using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published after an order is successfully created (§2.5). Decided (§8): the automatic
/// "one order placed → one billable unit" UC2 hook subscribes to this to record one usage unit
/// on the buyer's active subscription, if they have one. Delivery is best-effort, in-process only;
/// a subscriber failing here must never affect the order that already succeeded.
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
