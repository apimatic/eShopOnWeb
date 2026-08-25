using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId && !p.IsDeleted);
    }
}

public class SavedPaymentMethodByIdAndBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdAndBuyerSpec(int id, string buyerId)
    {
        Query.Where(p => p.Id == id && p.BuyerId == buyerId && !p.IsDeleted);
    }
}
