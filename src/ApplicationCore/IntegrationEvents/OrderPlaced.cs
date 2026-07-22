using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a customer completed the ordinary one-time checkout. The subscription module
/// listens for this to turn "one order placed" into one billable metered unit (UC2).
/// </summary>
/// <remarks>
/// Published in-process through MediatR once the order has already been persisted. Delivery is
/// best-effort by design: a failing handler must never roll back or block the order.
/// </remarks>
public class OrderPlaced : INotification
{
    public OrderPlaced(int orderId, string buyerId)
    {
        OrderId = orderId;
        BuyerId = buyerId;
    }

    public int OrderId { get; }

    /// <summary>The eShopOnWeb user reference the order was placed for.</summary>
    public string BuyerId { get; }
}
