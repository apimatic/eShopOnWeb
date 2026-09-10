using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderPaymentByOrderIdSpec : Specification<OrderPayment>
{
    public OrderPaymentByOrderIdSpec(int orderId) =>
        Query.Where(p => p.OrderId == orderId).Include(p => p.Refunds);
}

public class OrderPaymentsByBuyerSpec : Specification<OrderPayment>
{
    public OrderPaymentsByBuyerSpec(string buyerId) =>
        Query.Where(p => p.BuyerId == buyerId).Include(p => p.Refunds);
}

public class AllOrderPaymentsSpec : Specification<OrderPayment>
{
    public AllOrderPaymentsSpec() => Query.Include(p => p.Refunds);
}

public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId) =>
        Query.Where(m => m.BuyerId == buyerId);
}

public class SavedPaymentMethodByIdSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpec(int paymentMethodId) =>
        Query.Where(m => m.Id == paymentMethodId);
}
