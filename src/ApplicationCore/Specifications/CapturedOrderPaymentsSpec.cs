using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All order payments that have a PayPal capture, for operator reconciliation.</summary>
public sealed class CapturedOrderPaymentsSpec : Specification<OrderPayment>
{
    public CapturedOrderPaymentsSpec()
    {
        Query
            .Where(p => p.CaptureId != null)
            .Include(p => p.Refunds);
    }
}
