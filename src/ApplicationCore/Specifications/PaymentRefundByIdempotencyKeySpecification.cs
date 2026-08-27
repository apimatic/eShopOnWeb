using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentRefundByIdempotencyKeySpecification : Specification<PaymentRefund>
{
    public PaymentRefundByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(r => r.IdempotencyKey == idempotencyKey);
    }
}
