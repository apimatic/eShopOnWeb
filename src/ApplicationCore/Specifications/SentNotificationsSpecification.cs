using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every notification eShop believes it handed to the provider (it carries a provider message id).</summary>
public class SentNotificationsSpecification : Specification<SmsNotification>
{
    public SentNotificationsSpecification()
    {
        Query.Where(n => n.ProviderMessageSid != null);
    }
}
