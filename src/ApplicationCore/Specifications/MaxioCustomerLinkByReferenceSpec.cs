using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The local customer link for one Maxio customer reference (== one eShop user id).
/// </summary>
public class MaxioCustomerLinkByReferenceSpec : Specification<MaxioCustomerLink>
{
    public MaxioCustomerLinkByReferenceSpec(string reference)
    {
        Query.Where(link => link.MaxioReference == reference);
    }
}