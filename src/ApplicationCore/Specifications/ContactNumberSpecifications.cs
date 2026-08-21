using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.Id);
    }
}

public class ContactNumberByBuyerAndPhoneSpecification : Specification<ContactNumber>
{
    public ContactNumberByBuyerAndPhoneSpecification(string buyerId, string phoneNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.PhoneNumber == phoneNumber);
    }
}

public class ContactNumberByIdAndBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdAndBuyerSpecification(int contactNumberId, string buyerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.BuyerId == buyerId);
    }
}
