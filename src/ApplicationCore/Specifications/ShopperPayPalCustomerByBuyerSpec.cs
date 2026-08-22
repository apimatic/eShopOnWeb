using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ShopperPayPalCustomerByBuyerSpec : Specification<ShopperPayPalCustomer>
{
    public ShopperPayPalCustomerByBuyerSpec(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId);
    }
}
