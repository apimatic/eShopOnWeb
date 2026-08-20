using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByProviderSidsSpecification : Specification<OrderNotification>
{
    public NotificationsByProviderSidsSpecification(IEnumerable<string> providerSids)
    {
        var sids = providerSids.ToArray();
        Query.Where(n => n.ProviderSid != null && sids.Contains(n.ProviderSid));
    }
}
