using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByProviderSidsSpecification : Specification<OrderNotification>
{
    public NotificationsByProviderSidsSpecification(IEnumerable<string> sids)
    {
        var sidList = sids.ToArray();
        Query.Where(n => n.ProviderMessageSid != null && sidList.Contains(n.ProviderMessageSid));
    }
}
