using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByProviderSidsSpec : Specification<OrderNotification>
{
    public NotificationsByProviderSidsSpec(IReadOnlyCollection<string> providerMessageSids)
    {
        Query.Where(n => n.ProviderMessageSid != null && providerMessageSids.Contains(n.ProviderMessageSid));
    }
}
