using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a customer completed checkout. UC2 wires this to pay-as-you-go metering:
/// one order placed records one billable unit against the buyer's subscription.
/// </summary>
public class OrderPlaced : INotification
{
    public OrderPlaced(Order order)
    {
        Order = order;
    }

    public Order Order { get; }

    /// <summary>The eShopOnWeb user reference the order belongs to.</summary>
    public string BuyerId => Order.BuyerId;
}
