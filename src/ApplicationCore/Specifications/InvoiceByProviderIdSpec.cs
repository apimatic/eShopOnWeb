using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Finds the eShop bill that carries the given provider identifier.</summary>
public sealed class InvoiceByProviderIdSpec : Specification<Invoice>, ISingleResultSpecification<Invoice>
{
    public InvoiceByProviderIdSpec(string providerInvoiceId)
    {
        Query.Where(invoice => invoice.ProviderInvoiceId == providerInvoiceId);
    }
}
