using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Finds the mapping between a supplier's product (by the supplier's own identifier/URL) and the
/// catalog item it was imported into, so a re-sync can update that same item.
/// </summary>
public class SupplierProductMapSpecification : Specification<SupplierProductMap>
{
    public SupplierProductMapSpecification(int supplierId, string externalId)
    {
        Query.Where(map => map.SupplierId == supplierId && map.ExternalId == externalId);
    }
}
