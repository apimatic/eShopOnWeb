using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(m => m.BuyerId == buyerId)
            .OrderByDescending(m => m.Id);
    }
}

public class SavedPaymentMethodByIdAndBuyerSpec : Specification<SavedPaymentMethod>, ISingleResultSpecification
{
    public SavedPaymentMethodByIdAndBuyerSpec(int id, string buyerId)
    {
        Query.Where(m => m.Id == id && m.BuyerId == buyerId);
    }
}

public class SavedPaymentMethodByPaypalTokenSpec : Specification<SavedPaymentMethod>, ISingleResultSpecification
{
    public SavedPaymentMethodByPaypalTokenSpec(string paypalPaymentTokenId)
    {
        Query.Where(m => m.PaypalPaymentTokenId == paypalPaymentTokenId);
    }
}
