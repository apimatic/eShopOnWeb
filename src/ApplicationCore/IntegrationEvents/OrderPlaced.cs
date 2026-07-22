using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a shopper completed checkout. Subscription billing listens for this to record one
/// billable unit of metered usage per order (UC2). Publication is best-effort and never affects the
/// order itself.
/// </summary>
public class OrderPlaced : INotification
{
    public OrderPlaced(int orderId, string buyerId)
    {
        OrderId = orderId;
        BuyerId = buyerId;
    }

    public int OrderId { get; }

    /// <summary>The eShopOnWeb identity that placed the order (email / username).</summary>
    public string BuyerId { get; }
}
