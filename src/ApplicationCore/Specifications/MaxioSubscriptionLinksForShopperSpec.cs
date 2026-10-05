using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// All subscription links recorded for one eShop user on one product handle — used to count
/// previously used references and to find a still-live subscription for idempotent re-subscribe.
/// </summary>
public class MaxioSubscriptionLinksForShopperSpec : Specification<MaxioSubscriptionLink>
{
    public MaxioSubscriptionLinksForShopperSpec(string userId, string productHandle)
    {
        Query.Where(link => link.UserId == userId && link.ProductHandle == productHandle);
    }
}