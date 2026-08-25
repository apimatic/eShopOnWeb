using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentRecordByOrderIdSpec : Specification<PaymentRecord>
{
    public PaymentRecordByOrderIdSpec(int orderId)
    {
        Query.Where(p => p.OrderId == orderId);
    }
}

public class PaymentRecordByOrderIdSpecWithRefunds : Specification<PaymentRecord>
{
    public PaymentRecordByOrderIdSpecWithRefunds(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
             .Include(p => p.Refunds);
    }
}
