using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerIdSpecification : Specification<ShopperContactNumber>
{
    public ContactNumbersByBuyerIdSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
            .OrderByDescending(n => n.RegisteredAt);
    }
}

public class ContactNumberByIdSpecification : Specification<ShopperContactNumber>, ISingleResultSpecification<ShopperContactNumber>
{
    public ContactNumberByIdSpecification(int contactNumberId)
    {
        Query.Where(n => n.Id == contactNumberId);
    }
}

public class ContactNumberByBuyerAndCanonicalNumberSpecification : Specification<ShopperContactNumber>, ISingleResultSpecification<ShopperContactNumber>
{
    public ContactNumberByBuyerAndCanonicalNumberSpecification(string buyerId, string canonicalPhoneNumber)
    {
        Query.Where(n => n.BuyerId == buyerId && n.PhoneNumber == canonicalPhoneNumber);
    }
}

public class ContactNumberByCanonicalNumberSpecification : Specification<ShopperContactNumber>, ISingleResultSpecification<ShopperContactNumber>
{
    public ContactNumberByCanonicalNumberSpecification(string canonicalPhoneNumber)
    {
        Query.Where(n => n.PhoneNumber == canonicalPhoneNumber);
    }
}
