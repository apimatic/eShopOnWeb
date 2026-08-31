using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class InvoiceByIdSpecification : Specification<Invoice>, ISingleResultSpecification<Invoice>
{
    public InvoiceByIdSpecification(int invoiceId)
    {
        Query.Where(i => i.Id == invoiceId);
    }
}
