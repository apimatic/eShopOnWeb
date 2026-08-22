using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByProviderSidsSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByProviderSidsSpecification(IReadOnlyCollection<string> providerMessageSids)
    {
        Query.Where(n => n.ProviderMessageSid != null && providerMessageSids.Contains(n.ProviderMessageSid));
    }
}
