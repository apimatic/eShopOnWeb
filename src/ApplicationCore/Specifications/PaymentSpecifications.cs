using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentByOrderIdSpec : Specification<Payment>
{
    public PaymentByOrderIdSpec(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

public class PaymentsByBuyerSpec : Specification<Payment>
{
    public PaymentsByBuyerSpec(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}

public class PaymentsUpdatedInRangeSpec : Specification<Payment>
{
    public PaymentsUpdatedInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p => p.UpdatedAt >= from && p.UpdatedAt <= to)
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

public class SavedPaymentMethodByIdAndBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdAndBuyerSpec(int paymentMethodId, string buyerId)
    {
        Query.Where(m => m.Id == paymentMethodId && m.BuyerId == buyerId);
    }
}
