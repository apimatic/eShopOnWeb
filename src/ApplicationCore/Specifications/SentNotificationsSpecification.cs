using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications eShop believes it handed to the provider (i.e. that have a provider SID).</summary>
public class SentNotificationsSpecification : Specification<OrderNotification>
{
    public SentNotificationsSpecification()
    {
        Query.Where(n => n.ProviderMessageSid != null);
    }
}
