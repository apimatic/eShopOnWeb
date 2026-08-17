using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// eShop's side of reconciliation: messages that were handed to the provider (have a provider id)
/// and were created within the range.
/// </summary>
public class SmsNotificationsSentBetweenSpecification : Specification<SmsNotification>
{
    public SmsNotificationsSentBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageId != null
            && n.CreatedAt >= from
            && n.CreatedAt <= to);
    }
}
