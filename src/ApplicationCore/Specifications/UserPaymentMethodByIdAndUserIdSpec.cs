using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class UserPaymentMethodByIdAndUserIdSpec : Specification<UserPaymentMethod>, ISingleResultSpecification<UserPaymentMethod>
{
    public UserPaymentMethodByIdAndUserIdSpec(int id, string userId)
    {
        Query.Where(pm => pm.Id == id && pm.UserId == userId);
    }
}
