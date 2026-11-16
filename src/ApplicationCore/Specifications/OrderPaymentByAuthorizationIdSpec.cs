using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderPaymentByAuthorizationIdSpec : Specification<OrderPayment>
{
    public OrderPaymentByAuthorizationIdSpec(string authorizationId)
    {
        Query.Where(p => p.AuthorizationId == authorizationId);
    }
}
