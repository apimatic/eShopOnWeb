using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByProviderSidsSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByProviderSidsSpecification(string[] providerSids)
    {
        Query.Where(n => n.ProviderMessageSid != null && providerSids.Contains(n.ProviderMessageSid));
    }
}
