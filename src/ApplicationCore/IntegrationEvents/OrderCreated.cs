using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after an eShopOnWeb order has been persisted. It is the hook that turns
/// "one order placed" into "one billable unit" for the pay-as-you-go component (UC2).
/// </summary>
/// <remarks>
/// Publication is best-effort and deliberately non-blocking for the order lifecycle: a handler that
/// throws must never fail or roll back <c>OrderService.CreateOrderAsync</c>.
/// </remarks>
public class OrderCreated : INotification
{
    public OrderCreated(int orderId, string buyerId)
    {
        OrderId = orderId;
        BuyerId = buyerId;
    }

    public int OrderId { get; }

    /// <summary>
    /// The basket's buyer id, which for a signed-in shopper is the user's email/username — the same
    /// stable reference the billing provider's customer record is keyed on.
    /// </summary>
    public string BuyerId { get; }
}
