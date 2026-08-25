using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentRecordByOrderIdSpec : Specification<PaymentRecord>
{
    public PaymentRecordByOrderIdSpec(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
             .Include(p => p.Refunds);
    }
}

public class PaymentRecordByOrderAndBuyerSpec : Specification<PaymentRecord>
{
    public PaymentRecordByOrderAndBuyerSpec(int orderId, string buyerId)
    {
        Query.Where(p => p.OrderId == orderId && p.BuyerId == buyerId)
             .Include(p => p.Refunds);
    }
}

public class PaymentRecordsByBuyerSpec : Specification<PaymentRecord>
{
    public PaymentRecordsByBuyerSpec(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
             .Include(p => p.Refunds);
    }
}
