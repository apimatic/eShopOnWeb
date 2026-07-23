using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process when eShopOnWeb's checkout creates an order. The UC2 usage handler reacts to
/// it by recording one billable unit against the buyer's subscription.
/// </summary>
public class OrderPlaced : INotification
{
    public OrderPlaced(int orderId, string buyerId)
    {
        OrderId = orderId;
        BuyerId = buyerId;
    }

    public int OrderId { get; }

    /// <summary>The buyer's eShopOnWeb reference — the same value used as the billing customer reference.</summary>
    public string BuyerId { get; }
}
