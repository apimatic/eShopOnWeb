using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentAttemptsForOrderSpec : Specification<PaymentAttempt>
{
    public PaymentAttemptsForOrderSpec(int orderId)
    {
        Query.Where(a => a.OrderId == orderId).OrderBy(a => a.AttemptNumber);
    }
}

public class PaymentAttemptsForOrdersSpec : Specification<PaymentAttempt>
{
    public PaymentAttemptsForOrdersSpec(int[] orderIds)
    {
        Query.Where(a => orderIds.Contains(a.OrderId)).OrderBy(a => a.AttemptNumber);
    }
}

public class RefundsForOrderSpec : Specification<OrderRefund>
{
    public RefundsForOrderSpec(int orderId)
    {
        Query.Where(r => r.OrderId == orderId).OrderBy(r => r.CreatedDate);
    }
}

public class RefundsForOrdersSpec : Specification<OrderRefund>
{
    public RefundsForOrdersSpec(int[] orderIds)
    {
        Query.Where(r => orderIds.Contains(r.OrderId)).OrderBy(r => r.CreatedDate);
    }
}
