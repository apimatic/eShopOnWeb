using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentsByOrderIdSpecification : Specification<Payment>
{
    public PaymentsByOrderIdSpecification(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds)
            .OrderBy(p => p.Id);
    }
}

public class PaymentsByBuyerSpecification : Specification<Payment>
{
    public PaymentsByBuyerSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}

public class AllPaymentsWithRefundsSpecification : Specification<Payment>
{
    public AllPaymentsWithRefundsSpecification()
    {
        Query.Include(p => p.Refunds);
    }
}

public class PaymentsCapturedInRangeSpecification : Specification<Payment>
{
    public PaymentsCapturedInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p => p.CapturedAt.HasValue && p.CapturedAt.Value >= from && p.CapturedAt.Value <= to)
            .Include(p => p.Refunds);
    }
}
