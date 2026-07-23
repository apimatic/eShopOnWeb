using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a one-time order was placed. This is the hook UC2 uses to bill pay-as-you-go
/// usage automatically: one order placed records one billable unit (§8).
/// </summary>
/// <remarks>
/// Publication is best-effort and deliberately cannot fail order creation — a billing problem must
/// never roll back or block eShopOnWeb's existing order lifecycle.
/// </remarks>
public class OrderPlaced : INotification
{
    public OrderPlaced(string buyerId, int orderId)
    {
        BuyerId = buyerId;
        OrderId = orderId;
    }

    /// <summary>The eShopOnWeb user who placed the order (username/email).</summary>
    public string BuyerId { get; }

    public int OrderId { get; }
}
