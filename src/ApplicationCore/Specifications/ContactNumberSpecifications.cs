using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerIdSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerIdSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
            .OrderByDescending(n => n.Id);
    }
}

public class ContactNumberByBuyerAndCanonicalNumberSpecification : Specification<ContactNumber>
{
    public ContactNumberByBuyerAndCanonicalNumberSpecification(string buyerId, string phoneNumber)
    {
        Query.Where(n => n.BuyerId == buyerId && n.PhoneNumber == phoneNumber);
    }
}

public class ContactNumberByIdAndBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdAndBuyerSpecification(int contactNumberId, string buyerId)
    {
        Query.Where(n => n.Id == contactNumberId && n.BuyerId == buyerId);
    }
}
