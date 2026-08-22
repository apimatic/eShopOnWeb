using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersByPayPalIdentifiersSpecification : Specification<Order>
{
    public OrdersByPayPalIdentifiersSpecification(IReadOnlyCollection<string> payPalIds)
    {
        var ids = payPalIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        Query
            .Include(o => o.Refunds)
            .Where(o =>
                (o.Payment.PayPalOrderId != null && ids.Contains(o.Payment.PayPalOrderId)) ||
                (o.Payment.AuthorizationId != null && ids.Contains(o.Payment.AuthorizationId)) ||
                (o.Payment.CaptureId != null && ids.Contains(o.Payment.CaptureId)) ||
                o.Refunds.Any(r => ids.Contains(r.PayPalRefundId)));
    }
}
