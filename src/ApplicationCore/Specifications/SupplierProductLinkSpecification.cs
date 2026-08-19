using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Finds the link between a supplier's product (by its external id) and the catalog item it
/// was imported into. Used to make re-syncs idempotent.
/// </summary>
public class SupplierProductLinkSpecification : Specification<SupplierProductLink>
{
    public SupplierProductLinkSpecification(Guid supplierId, string externalId)
    {
        Query.Where(l => l.SupplierId == supplierId && l.ExternalId == externalId);
    }

    public SupplierProductLinkSpecification(Guid supplierId)
    {
        Query.Where(l => l.SupplierId == supplierId);
    }
}
