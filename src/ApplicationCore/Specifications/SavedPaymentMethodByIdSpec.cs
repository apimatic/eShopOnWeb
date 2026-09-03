using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A saved card by id, scoped to its owner so one shopper can never act on another's card.</summary>
public sealed class SavedPaymentMethodByIdSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpec(int paymentMethodId, string buyerId)
    {
        Query.Where(m => m.Id == paymentMethodId && m.BuyerId == buyerId);
    }
}
