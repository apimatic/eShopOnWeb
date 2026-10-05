using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The local subscription link for one Maxio subscription reference.
/// </summary>
public class MaxioSubscriptionLinkByReferenceSpec : Specification<MaxioSubscriptionLink>
{
    public MaxioSubscriptionLinkByReferenceSpec(string reference)
    {
        Query.Where(link => link.MaxioReference == reference);
    }
}