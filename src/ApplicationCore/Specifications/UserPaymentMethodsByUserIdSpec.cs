using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class UserPaymentMethodsByUserIdSpec : Specification<UserPaymentMethod>
{
    public UserPaymentMethodsByUserIdSpec(string userId)
    {
        Query.Where(pm => pm.UserId == userId);
    }
}
