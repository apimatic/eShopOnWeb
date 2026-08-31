using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentByOrderIdSpecification : Specification<Payment>
{
    public PaymentByOrderIdSpecification(int orderId)
    {
        Query
            .Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds)
            .OrderByDescending(p => p.Id);
    }
}

public class PaymentsInDateRangeSpecification : Specification<Payment>
{
    public PaymentsInDateRangeSpecification(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query
            .Where(p => p.AuthorizedAt >= from && p.AuthorizedAt <= to)
            .Include(p => p.Refunds);
    }
}
