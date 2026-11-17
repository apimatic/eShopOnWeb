using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedCardsByOwnerSpecification : Specification<SavedCard>
{
    public SavedCardsByOwnerSpecification(string ownerId)
    {
        Query.Where(c => c.OwnerId == ownerId);
    }
}

public class SavedCardByIdAndOwnerSpecification : Specification<SavedCard>
{
    public SavedCardByIdAndOwnerSpecification(int paymentMethodId, string ownerId)
    {
        Query.Where(c => c.Id == paymentMethodId && c.OwnerId == ownerId);
    }
}

public class PaymentCustomerByOwnerSpecification : Specification<PaymentCustomer>
{
    public PaymentCustomerByOwnerSpecification(string ownerId)
    {
        Query.Where(c => c.OwnerId == ownerId);
    }
}
