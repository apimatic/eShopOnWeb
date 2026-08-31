using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The local record of a bill, looked up by the provider's invoice id (the public identifier).</summary>
public sealed class InvoiceByProviderIdSpecification : Specification<Invoice>
{
    public InvoiceByProviderIdSpecification(string providerInvoiceId)
    {
        Query.Where(i => i.ProviderInvoiceId == providerInvoiceId);
    }
}
