using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The still-scheduled (not yet sent) messages queued with the provider for a given shopper and
/// destination number. Used when a shopper removes a number so nothing already queued reaches it.
/// </summary>
public class ScheduledNotificationsForContactNumberSpecification : Specification<OrderNotification>
{
    public ScheduledNotificationsForContactNumberSpecification(string buyerId, string toNumber)
    {
        Query.Where(n => n.BuyerId == buyerId
            && n.ToNumber == toNumber
            && n.IsScheduled
            && n.ProviderSid != null);
    }
}
