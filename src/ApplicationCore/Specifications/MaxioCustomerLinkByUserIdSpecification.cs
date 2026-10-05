using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Fetches the Maxio customer link for an eShopOnWeb identity user id.
/// </summary>
public sealed class MaxioCustomerLinkByUserIdSpecification : Specification<MaxioCustomerLink>
{
    public MaxioCustomerLinkByUserIdSpecification(string userId)
    {
        Query.Where(link => link.UserId == userId);
    }
}