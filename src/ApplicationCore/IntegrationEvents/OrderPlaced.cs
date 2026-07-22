using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after an order is created, so subscription usage can be metered from checkout
/// (one order placed → one billable unit). Delivery is best-effort and never blocks or rolls back the
/// order lifecycle.
/// </summary>
public class OrderPlaced : INotification
{
    public OrderPlaced(string buyerId, int orderId)
    {
        BuyerId = buyerId;
        OrderId = orderId;
    }

    /// <summary>
    /// The eShopOnWeb buyer identifier — the same email/username used as the billing customer reference.
    /// </summary>
    public string BuyerId { get; }

    public int OrderId { get; }
}
