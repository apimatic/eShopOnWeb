using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderPaymentByOrderIdSpec : Specification<OrderPayment>
{
    public OrderPaymentByOrderIdSpec(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

public class OrderPaymentsInRangeSpec : Specification<OrderPayment>
{
    public OrderPaymentsInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p => p.CreatedAt >= from && p.CreatedAt <= to)
            .Include(p => p.Refunds);
    }
}

public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(m => m.BuyerId == buyerId);
    }
}

public class SavedPaymentMethodByIdSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpec(int paymentMethodId)
    {
        Query.Where(m => m.Id == paymentMethodId);
    }
}
