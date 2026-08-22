using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.Id);
    }
}

public class ContactNumberByBuyerAndE164Specification : Specification<ContactNumber>
{
    public ContactNumberByBuyerAndE164Specification(string buyerId, string phoneNumberE164)
    {
        Query.Where(c => c.BuyerId == buyerId && c.PhoneNumberE164 == phoneNumberE164);
    }
}

public class ContactNumberByIdAndBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdAndBuyerSpecification(int contactNumberId, string buyerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.BuyerId == buyerId);
    }
}

public class ContactNumberStillRegisteredSpecification : Specification<ContactNumber>
{
    public ContactNumberStillRegisteredSpecification(string buyerId, string phoneNumberE164)
    {
        Query.Where(c => c.BuyerId == buyerId && c.PhoneNumberE164 == phoneNumberE164);
    }
}
