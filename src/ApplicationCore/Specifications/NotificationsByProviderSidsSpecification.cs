using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByProviderSidsSpecification : Specification<OrderNotification>
{
    public NotificationsByProviderSidsSpecification(IEnumerable<string> sids)
    {
        var list = sids.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        Query.Where(n => n.ProviderMessageSid != null && list.Contains(n.ProviderMessageSid));
    }
}
