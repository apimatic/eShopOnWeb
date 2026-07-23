using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after an eShopOnWeb order has been created. The subscription feature listens for
/// it so that one placed order records one billable unit of metered usage (plan.md §8, UC2).
/// </summary>
/// <remarks>
/// Publication is best-effort and deliberately decoupled: the order stands whatever any handler does, so
/// a billing outage can never block or roll back checkout.
/// </remarks>
public class OrderPlaced : INotification
{
    public OrderPlaced(int orderId, string buyerId)
    {
        OrderId = orderId;
        BuyerId = buyerId;
    }

    public int OrderId { get; }

    /// <summary>The eShopOnWeb user name (email) that placed the order.</summary>
    public string BuyerId { get; }
}
