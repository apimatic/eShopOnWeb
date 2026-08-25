using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderWithPaymentSpec : Specification<Order>
{
    public OrderWithPaymentSpec(int orderId)
    {
        Query.Where(o => o.Id == orderId)
            .Include(o => o.OrderItems)
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}

public class OrderWithPaymentByBuyerSpec : Specification<Order>
{
    public OrderWithPaymentByBuyerSpec(int orderId, string buyerId)
    {
        Query.Where(o => o.Id == orderId && o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}

public class CustomerOrdersWithPaymentSpec : Specification<Order>
{
    public CustomerOrdersWithPaymentSpec(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds)
            .OrderByDescending(o => o.OrderDate);
    }
}

public class PaymentByOrderIdSpec : Specification<Payment>
{
    public PaymentByOrderIdSpec(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

public class AllOrdersWithPaymentSpec : Specification<Order>
{
    public AllOrdersWithPaymentSpec()
    {
        Query.Include(o => o.Payment!)
             .ThenInclude(p => p.Refunds);
    }
}

public class BuyerByIdentitySpec : Specification<ApplicationCore.Entities.BuyerAggregate.Buyer>
{
    public BuyerByIdentitySpec(string identityGuid)
    {
        Query.Where(b => b.IdentityGuid == identityGuid)
            .Include(b => b.PaymentMethods);
    }
}
