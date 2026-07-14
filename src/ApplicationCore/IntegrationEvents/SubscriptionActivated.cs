using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published after a customer is successfully enrolled in a plan (UC1).</summary>
public class SubscriptionActivated : INotification
{
    public int SubscriptionId { get; }
    public string UserName { get; }
    public string ProductHandle { get; }
    public int PriceInCents { get; }
    public DateTimeOffset? NextAssessmentAt { get; }

    public SubscriptionActivated(int subscriptionId, string userName, string productHandle, int priceInCents, DateTimeOffset? nextAssessmentAt)
    {
        SubscriptionId = subscriptionId;
        UserName = userName;
        ProductHandle = productHandle;
        PriceInCents = priceInCents;
        NextAssessmentAt = nextAssessmentAt;
    }
}
