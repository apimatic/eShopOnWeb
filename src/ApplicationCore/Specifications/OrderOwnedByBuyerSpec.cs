using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Matches the order only when it belongs to the given buyer. Used for existence checks that must not
/// reveal whether another shopper's order exists.
/// </summary>
public class OrderOwnedByBuyerSpec : Specification<Order>
{
    public OrderOwnedByBuyerSpec(int orderId, string buyerId)
    {
        Query.Where(o => o.Id == orderId && o.BuyerId == buyerId);
    }
}
