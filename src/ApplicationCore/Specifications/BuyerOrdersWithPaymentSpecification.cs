using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// All of a buyer's orders with items and payment state, newest first.
/// </summary>
public class BuyerOrdersWithPaymentSpecification : Specification<Order>
{
    public BuyerOrdersWithPaymentSpecification(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds)
            .OrderByDescending(o => o.OrderDate);
    }
}
