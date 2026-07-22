using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published after an eShopOnWeb order has been created. Carries the automatic
/// "one order placed → one billable unit" trigger for pay-as-you-go usage (plan.md §8, UC2).
/// </summary>
/// <remarks>
/// This announces a fact about eShopOnWeb's own order lifecycle. Handlers must treat it as
/// best-effort: the order is already persisted by the time it is published, and nothing a handler
/// does may roll it back.
/// </remarks>
public class OrderPlaced : INotification
{
    public OrderPlaced(int orderId, string buyerId)
    {
        OrderId = orderId;
        BuyerId = buyerId;
    }

    public int OrderId { get; }

    /// <summary>The buyer the order belongs to — the same reference the billing customer uses.</summary>
    public string BuyerId { get; }
}
