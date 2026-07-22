using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that an eShopOnWeb order was created. Subscriptions listen for this to meter
/// pay-as-you-go usage (UC2); nothing in the order lifecycle depends on a handler succeeding.
/// </summary>
public class OrderPlaced : INotification
{
    public OrderPlaced(int orderId, string buyerId)
    {
        OrderId = orderId;
        BuyerId = buyerId;
    }

    public int OrderId { get; }

    /// <summary>The basket's buyer id — the signed-in user's username for authenticated checkouts.</summary>
    public string BuyerId { get; }
}
