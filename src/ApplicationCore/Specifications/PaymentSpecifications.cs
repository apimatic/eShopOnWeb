using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentByOrderIdSpecification : Specification<Payment>
{
    public PaymentByOrderIdSpecification(int orderId)
    {
        Query.Where(p => p.OrderId == orderId);
    }
}

public class PaymentsByOwnerSpecification : Specification<Payment>
{
    public PaymentsByOwnerSpecification(string ownerId)
    {
        Query.Where(p => p.OwnerId == ownerId);
    }
}

public class PaymentsByOrderIdsSpecification : Specification<Payment>
{
    public PaymentsByOrderIdsSpecification(IReadOnlyCollection<int> orderIds)
    {
        Query.Where(p => orderIds.Contains(p.OrderId));
    }
}
