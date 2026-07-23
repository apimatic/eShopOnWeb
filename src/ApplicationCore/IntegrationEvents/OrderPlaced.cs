using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after an eShopOnWeb order has been created.
/// </summary>
/// <remarks>
/// This is the hook the pay-as-you-go use case listens on: one order placed records one billable
/// unit of metered usage. Publication is best-effort — handler failures are contained by the
/// publisher so a billing problem can never fail or roll back the order that was already created.
/// </remarks>
public class OrderPlaced : INotification
{
    public OrderPlaced(int orderId, string buyerId)
    {
        OrderId = orderId;
        BuyerId = buyerId;
    }

    public int OrderId { get; }

    /// <summary>The eShopOnWeb buyer reference (email / username) the order was placed for.</summary>
    public string BuyerId { get; }
}
