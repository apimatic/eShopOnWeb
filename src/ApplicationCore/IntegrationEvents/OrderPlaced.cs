using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a customer completed checkout. Subscriptions listen for this to bill one unit of
/// pay-as-you-go usage per order placed (§8, UC2 trigger).
/// </summary>
public class OrderPlaced : INotification
{
    public OrderPlaced(Order order)
    {
        Order = order;
    }

    public Order Order { get; }

    /// <summary>The eShopOnWeb user who placed the order — the billing customer reference (§4.4).</summary>
    public string BuyerId => Order.BuyerId;
}
