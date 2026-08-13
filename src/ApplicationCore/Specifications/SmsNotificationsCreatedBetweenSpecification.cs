using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// eShop's own record of messages it believes it handed to the provider within a window, used by
/// the reconciliation report. Only notifications that reached the provider (have a message sid)
/// are relevant to line up against the provider's records.
/// </summary>
public class SmsNotificationsCreatedBetweenSpecification : Specification<SmsNotification>
{
    public SmsNotificationsCreatedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null &&
                         n.CreatedDate >= from && n.CreatedDate <= to)
            .OrderBy(n => n.CreatedDate);
    }
}
