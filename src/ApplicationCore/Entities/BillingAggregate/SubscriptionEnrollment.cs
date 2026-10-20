using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// One subscription of one user to one plan. <see cref="BillingClaim.Key"/> is
/// "{customerReference}:{planHandle}", which is also the subscription reference sent to the provider.
/// </summary>
public class SubscriptionEnrollment : BillingClaim
{
    private SubscriptionEnrollment()
    {
        // required by EF
        CustomerReference = string.Empty;
        PlanHandle = string.Empty;
    }

    public SubscriptionEnrollment(string customerReference, string planHandle, DateTimeOffset now)
        : base(BuildKey(customerReference, planHandle), now)
    {
        CustomerReference = customerReference;
        PlanHandle = planHandle;
    }

    public string CustomerReference { get; private set; }
    public string PlanHandle { get; private set; }
    public int? ProviderSubscriptionId { get; private set; }

    public static string BuildKey(string customerReference, string planHandle) => $"{customerReference}:{planHandle}";

    public void MarkSubscribed(int providerSubscriptionId, DateTimeOffset now)
    {
        ProviderSubscriptionId = providerSubscriptionId;
        MarkCompletedCore(now);
    }
}
