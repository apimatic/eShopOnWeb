using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All of a shopper's registered numbers, newest first.</summary>
public sealed class ContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
             .OrderByDescending(c => c.RegisteredAt);
    }
}

/// <summary>A single number, scoped to its owning shopper (so one shopper can never act on another's).</summary>
public sealed class ContactNumberByBuyerAndIdSpecification : Specification<ContactNumber>
{
    public ContactNumberByBuyerAndIdSpecification(string buyerId, int contactNumberId)
    {
        Query.Where(c => c.BuyerId == buyerId && c.Id == contactNumberId);
    }
}

/// <summary>The shopper's most-recently-registered number — the one order messages are sent to.</summary>
public sealed class LatestContactNumberByBuyerSpecification : Specification<ContactNumber>
{
    public LatestContactNumberByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
             .OrderByDescending(c => c.RegisteredAt)
             .Take(1);
    }
}
