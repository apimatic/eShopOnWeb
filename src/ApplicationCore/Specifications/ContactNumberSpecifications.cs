using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ActiveContactNumbersByBuyerSpec : Specification<ContactNumber>
{
    public ActiveContactNumbersByBuyerSpec(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId && c.DeletedAt == null)
            .OrderByDescending(c => c.CreatedAt);
    }
}

public class ContactNumberByBuyerAndIdSpec : Specification<ContactNumber>, ISingleResultSpecification<ContactNumber>
{
    public ContactNumberByBuyerAndIdSpec(string buyerId, int contactNumberId)
    {
        Query.Where(c => c.BuyerId == buyerId && c.Id == contactNumberId);
    }
}

public class ContactNumberByBuyerAndPhoneSpec : Specification<ContactNumber>, ISingleResultSpecification<ContactNumber>
{
    public ContactNumberByBuyerAndPhoneSpec(string buyerId, string canonicalPhoneNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.PhoneNumber == canonicalPhoneNumber);
    }
}
