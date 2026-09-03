using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ActiveContactNumberByPhoneNumberAndBuyerSpecification : Specification<ContactNumber>
{
    public ActiveContactNumberByPhoneNumberAndBuyerSpecification(string phoneNumber, string buyerId)
    {
        Query.Where(c => c.PhoneNumber == phoneNumber
            && c.BuyerId == buyerId
            && c.DeletedAt == null);
    }
}
