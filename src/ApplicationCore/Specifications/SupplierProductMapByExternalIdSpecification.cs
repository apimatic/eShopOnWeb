using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SupplierProductMapByExternalIdSpecification : Specification<SupplierProductMap>
{
    public SupplierProductMapByExternalIdSpecification(int supplierId, string externalId)
    {
        Query.Where(m => m.SupplierId == supplierId && m.ExternalId == externalId);
    }
}
