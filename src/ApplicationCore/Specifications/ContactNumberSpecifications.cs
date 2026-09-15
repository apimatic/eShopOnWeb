using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The contact numbers registered by one shopper, newest first.</summary>
public class ContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
             .OrderByDescending(c => c.RegisteredDate);
    }
}

/// <summary>A single contact number, but only if it belongs to the given shopper.</summary>
public class ContactNumberByIdForBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdForBuyerSpecification(int contactNumberId, string buyerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.BuyerId == buyerId);
    }
}

/// <summary>A shopper's registration of a specific E.164 number (used to confirm a number is still on file).</summary>
public class ContactNumberByValueForBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumberByValueForBuyerSpecification(string buyerId, string phoneNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.PhoneNumber == phoneNumber);
    }
}
