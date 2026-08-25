using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerEmail)
    {
        Query.Where(p => p.BuyerEmail == buyerEmail);
    }
}

public class SavedPaymentMethodByIdAndBuyerSpec : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdAndBuyerSpec(int id, string buyerEmail)
    {
        Query.Where(p => p.Id == id && p.BuyerEmail == buyerEmail);
    }
}
