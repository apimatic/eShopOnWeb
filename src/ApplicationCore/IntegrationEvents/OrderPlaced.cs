using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published after an order is successfully created. Drives UC2's "one order placed → one
/// billable unit" demo hook: a handler records one usage unit against the buyer's active
/// subscription, if they have one.
/// </summary>
public class OrderPlaced : INotification
{
    public string BuyerId { get; }
    public int OrderId { get; }

    public OrderPlaced(string buyerId, int orderId)
    {
        BuyerId = buyerId;
        OrderId = orderId;
    }
}
