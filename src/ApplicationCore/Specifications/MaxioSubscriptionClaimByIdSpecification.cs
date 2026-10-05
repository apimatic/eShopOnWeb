using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Fetches the subscription claim row for a deterministic Maxio subscription reference.
/// </summary>
public sealed class MaxioSubscriptionClaimByIdSpecification : Specification<MaxioSubscriptionClaim>
{
    public MaxioSubscriptionClaimByIdSpecification(string requestId)
    {
        Query.Where(claim => claim.RequestId == requestId);
    }
}