using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(m => m.BuyerId == buyerId);
    }
}

public class SavedPaymentMethodByIdSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpec(int id)
    {
        Query.Where(m => m.Id == id);
    }
}
