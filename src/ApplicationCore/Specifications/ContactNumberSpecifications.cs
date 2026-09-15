using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerIdSpec : Specification<ContactNumber>
{
    public ContactNumbersByBuyerIdSpec(string buyerId)
    {
        Query.Where(number => number.BuyerId == buyerId)
            .OrderByDescending(number => number.Id);
    }
}

public class ContactNumberByIdAndBuyerSpec : Specification<ContactNumber>, ISingleResultSpecification<ContactNumber>
{
    public ContactNumberByIdAndBuyerSpec(int contactNumberId, string buyerId)
    {
        Query.Where(number => number.Id == contactNumberId && number.BuyerId == buyerId);
    }
}

public class ContactNumberByBuyerAndCanonicalSpec : Specification<ContactNumber>, ISingleResultSpecification<ContactNumber>
{
    public ContactNumberByBuyerAndCanonicalSpec(string buyerId, string canonicalPhoneNumber)
    {
        Query.Where(number => number.BuyerId == buyerId && number.PhoneNumber == canonicalPhoneNumber);
    }
}

public class ActiveContactNumberForDestinationSpec : Specification<ContactNumber>, ISingleResultSpecification<ContactNumber>
{
    public ActiveContactNumberForDestinationSpec(string buyerId, string canonicalPhoneNumber)
    {
        Query.Where(number => number.BuyerId == buyerId && number.PhoneNumber == canonicalPhoneNumber);
    }
}
