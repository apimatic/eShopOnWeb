using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierCatalogAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Finds the existing mapping for one supplier product by the supplier's own
/// identifier or URL, so a re-sync updates that catalog item instead of duplicating it.
/// </summary>
public class SupplierProductByExternalIdSpecification : Specification<SupplierProduct>
{
    public SupplierProductByExternalIdSpecification(Guid supplierId, string externalId)
    {
        Query.Where(sp => sp.SupplierId == supplierId && sp.ExternalId == externalId);
    }
}
