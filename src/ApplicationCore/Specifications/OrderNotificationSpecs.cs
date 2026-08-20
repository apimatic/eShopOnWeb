using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByOrderIdSpec : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.Id);
    }
}

public class OrderNotificationsByOrderIdsSpec : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdsSpec(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToArray();
        Query.Where(n => ids.Contains(n.OrderId))
            .OrderBy(n => n.Id);
    }
}

public class OrderNotificationsByProviderSidsSpec : Specification<OrderNotification>
{
    public OrderNotificationsByProviderSidsSpec(IEnumerable<string> providerSids)
    {
        var sids = providerSids.ToArray();
        Query.Where(n => n.ProviderSid != null && sids.Contains(n.ProviderSid));
    }
}

public class OrderNotificationsMissingFromProviderSpec : Specification<OrderNotification>
{
    public OrderNotificationsMissingFromProviderSpec(IEnumerable<string> providerSids)
    {
        var sids = providerSids.ToArray();
        Query.Where(n => n.ProviderSid != null && n.ProviderSid != "" && !sids.Contains(n.ProviderSid));
    }
}
