using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationByProviderSidSpecification : Specification<OrderNotification>
{
    public OrderNotificationByProviderSidSpecification(string providerMessageSid)
    {
        Query.Where(n => n.ProviderMessageSid == providerMessageSid);
    }
}
