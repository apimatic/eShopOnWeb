using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a one-time order was placed. The subscription feature listens for this to bill
/// a single pay-as-you-go unit per order (plan section 8), which is why it is published
/// best-effort: nothing a listener does may affect the order that has already been created.
/// </summary>
public class OrderPlaced : INotification
{
    public OrderPlaced(Order order)
    {
        Order = order;
    }

    public Order Order { get; }

    /// <summary>The eShopOnWeb user who placed the order; also the billing customer reference.</summary>
    public string BuyerId => Order.BuyerId;
}
