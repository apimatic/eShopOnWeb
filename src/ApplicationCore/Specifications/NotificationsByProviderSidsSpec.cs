using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByProviderSidsSpec : Specification<OrderNotification>
{
    public NotificationsByProviderSidsSpec(IEnumerable<string> sids)
    {
        var sidList = sids.ToList();
        Query.Where(n => n.ProviderMessageSid != null && sidList.Contains(n.ProviderMessageSid));
    }
}
