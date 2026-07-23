using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that an eShopOnWeb user was enrolled in a plan (UC1 step 6). Published in-process
/// through MediatR after the provider call succeeded; delivery is best-effort (§2.5).
/// </summary>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string buyerId,
        long subscriptionId,
        string planHandle,
        string planName,
        int planPriceInCents,
        DateTimeOffset? nextBillingDate)
    {
        BuyerId = buyerId;
        SubscriptionId = subscriptionId;
        PlanHandle = planHandle;
        PlanName = planName;
        PlanPriceInCents = planPriceInCents;
        NextBillingDate = nextBillingDate;
    }

    public string BuyerId { get; }

    public long SubscriptionId { get; }

    public string PlanHandle { get; }

    public string PlanName { get; }

    public int PlanPriceInCents { get; }

    public DateTimeOffset? NextBillingDate { get; }
}
