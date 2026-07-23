using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published when an order is created. The subscription module subscribes to this to record one
/// billable unit of metered usage per order placed (UC2's automatic trigger). Delivery is
/// in-process and best-effort: a failing handler never rolls the order back.
/// </summary>
public class OrderPlaced : INotification
{
    public OrderPlaced(Order order)
    {
        Order = order;
    }

    public Order Order { get; }

    /// <summary>The eShopOnWeb user the order belongs to — the same reference the billing provider customer uses.</summary>
    public string UserName => Order.BuyerId;
}
