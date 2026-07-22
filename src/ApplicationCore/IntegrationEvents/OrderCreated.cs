using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Raised in-process after an order is created (§8 / UC2 trigger). A usage handler turns each order
/// placed into one billable metered unit on the buyer's active subscription, if they have one.
/// </summary>
public class OrderCreated : INotification
{
    public OrderCreated(int orderId, string buyerId)
    {
        OrderId = orderId;
        BuyerId = buyerId;
    }

    public int OrderId { get; }

    /// <summary>The eShopOnWeb buyer reference (username/email) — the same reference used for billing (§4.4).</summary>
    public string BuyerId { get; }
}
