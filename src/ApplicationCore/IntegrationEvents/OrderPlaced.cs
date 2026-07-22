using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process when a customer completes checkout. The subscription module listens for
/// this to bill one metered unit per order placed (plan.md §8, UC2 trigger).
/// </summary>
public class OrderPlaced : INotification
{
    public OrderPlaced(int orderId, string buyerId)
    {
        OrderId = orderId;
        BuyerId = buyerId;
    }

    public int OrderId { get; }

    /// <summary>
    /// The eShopOnWeb identity that placed the order — the same reference the billing provider
    /// knows the customer by.
    /// </summary>
    public string BuyerId { get; }
}
