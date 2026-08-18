using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderPaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All payments with their refunds — used to line eShop settlements up during reconciliation.</summary>
public class OrderPaymentsWithRefundsSpecification : Specification<OrderPayment>
{
    public OrderPaymentsWithRefundsSpecification()
    {
        Query.Include(p => p.Refunds);
    }
}
