using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByProviderSidsSpecification : Specification<OrderNotification>
{
    public NotificationsByProviderSidsSpecification(string[] providerSids)
    {
        Query.Where(n => n.ProviderMessageSid != null && providerSids.Contains(n.ProviderMessageSid));
    }
}
