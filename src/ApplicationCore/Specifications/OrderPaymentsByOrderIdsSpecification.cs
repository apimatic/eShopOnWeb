using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Loads the payments (with their refunds) for a set of orders — used to project payment state onto a
/// shopper's order list without an N+1 query.</summary>
public class OrderPaymentsByOrderIdsSpecification : Specification<OrderPayment>
{
    public OrderPaymentsByOrderIdsSpecification(params int[] orderIds)
    {
        Query
            .Where(p => orderIds.Contains(p.OrderId))
            .Include(p => p.Refunds);
    }
}
