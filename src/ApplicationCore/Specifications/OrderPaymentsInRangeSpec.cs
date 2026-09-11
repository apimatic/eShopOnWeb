using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Payments created within a date range that have reached PayPal (have a PayPal order id).</summary>
public class OrderPaymentsInRangeSpec : Specification<OrderPayment>
{
    public OrderPaymentsInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(p => p.CreatedAt >= from && p.CreatedAt <= to && p.PayPalOrderId != null)
            .Include(p => p.Refunds);
    }
}
