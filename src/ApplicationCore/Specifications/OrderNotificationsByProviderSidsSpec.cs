using System.Collections.Generic;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByProviderSidsSpec : Specification<OrderNotification>
{
    public OrderNotificationsByProviderSidsSpec(IEnumerable<string> providerMessageSids)
    {
        var sidSet = providerMessageSids as IList<string> ?? new List<string>(providerMessageSids);
        Query.Where(n => n.ProviderMessageSid != null && sidSet.Contains(n.ProviderMessageSid));
    }
}
