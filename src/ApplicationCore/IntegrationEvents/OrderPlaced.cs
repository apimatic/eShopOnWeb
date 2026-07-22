using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process when a customer completes checkout. The subscription module listens for
/// it so that "one order placed" becomes one billable metered unit (plan §8, UC2 trigger).
/// </summary>
public class OrderPlaced : INotification
{
    public OrderPlaced(string buyerId)
    {
        BuyerId = buyerId;
    }

    /// <summary>The eShopOnWeb user reference the order was placed for.</summary>
    public string BuyerId { get; }
}
