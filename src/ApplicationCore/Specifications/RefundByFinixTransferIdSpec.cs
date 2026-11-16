using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class RefundByFinixTransferIdSpec : Specification<Refund>
{
    public RefundByFinixTransferIdSpec(string finixTransferId)
    {
        Query.Where(r => r.FinixRefundTransferId == finixTransferId);
    }
}
