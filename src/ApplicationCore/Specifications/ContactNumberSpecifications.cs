using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId);
    }
}

public class ContactNumberByIdSpecification : Specification<ContactNumber>, ISingleResultSpecification<ContactNumber>
{
    public ContactNumberByIdSpecification(int contactNumberId)
    {
        Query.Where(c => c.Id == contactNumberId);
    }
}

public class ContactNumberByBuyerAndNumberSpecification : Specification<ContactNumber>, ISingleResultSpecification<ContactNumber>
{
    public ContactNumberByBuyerAndNumberSpecification(string buyerId, string phoneNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.PhoneNumber == phoneNumber);
    }
}
