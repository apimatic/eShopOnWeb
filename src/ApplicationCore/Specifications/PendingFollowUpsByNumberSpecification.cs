using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PendingFollowUpsByNumberSpecification : Specification<OrderNotification>
{
    public PendingFollowUpsByNumberSpecification(string canonicalNumber)
    {
        Query.Where(n =>
            n.DestinationCanonical == canonicalNumber &&
            n.Kind == OrderNotificationKind.DeliveryFollowUp &&
            n.ProviderSid != null &&
            n.ProviderStatus == "scheduled");
    }
}
