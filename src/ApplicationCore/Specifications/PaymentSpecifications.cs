using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentByOrderIdSpec : Specification<Payment>
{
    public PaymentByOrderIdSpec(int orderId)
    {
        Query
            .Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

public class PaymentsByBuyerSpec : Specification<Payment>
{
    public PaymentsByBuyerSpec(string buyerId)
    {
        Query
            .Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}

/// <summary>
/// Payments that could have PayPal activity inside [from, to]: created before the window
/// closed. Ids are matched in memory against PayPal's transaction report.
/// </summary>
public class PaymentsForReconciliationSpec : Specification<Payment>
{
    public PaymentsForReconciliationSpec(DateTimeOffset to)
    {
        Query
            .Where(p => p.CreatedAt <= to)
            .Include(p => p.Refunds);
    }
}
