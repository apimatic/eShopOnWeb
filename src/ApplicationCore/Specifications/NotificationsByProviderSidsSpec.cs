using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByProviderSidsSpec : Specification<OrderNotification>
{
    public NotificationsByProviderSidsSpec(IReadOnlyCollection<string> sids)
    {
        Query.Where(n => n.ProviderSid != null && sids.Contains(n.ProviderSid));
    }
}
