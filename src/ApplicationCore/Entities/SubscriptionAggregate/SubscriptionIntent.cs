using System;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class SubscriptionIntent : BaseEntity, IAggregateRoot
{
    private SubscriptionIntent() { }

    public SubscriptionIntent(string userId, int productId, string providerReference)
    {
        UserId = userId;
        ProductId = productId;
        ProviderReference = providerReference;
        Status = SubscriptionIntentStatus.Pending;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public string UserId { get; private set; } = string.Empty;
    public int ProductId { get; private set; }
    public string ProviderReference { get; private set; } = string.Empty;
    public SubscriptionIntentStatus Status { get; private set; }
    public int? ProviderSubscriptionId { get; private set; }
    public string? PlanName { get; private set; }
    public string? PlanHandle { get; private set; }
    public long? PriceInCents { get; private set; }
    public string? ProviderState { get; private set; }
    public DateTimeOffset? NextBillingAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkSucceeded(SubscriptionDetails subscription)
    {
        Status = SubscriptionIntentStatus.Succeeded;
        ProviderSubscriptionId = subscription.Id;
        PlanName = subscription.PlanName;
        PlanHandle = subscription.PlanHandle;
        PriceInCents = subscription.PriceInCents;
        ProviderState = subscription.State;
        NextBillingAt = subscription.NextBillingAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkUnknown()
    {
        Status = SubscriptionIntentStatus.Unknown;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed()
    {
        Status = SubscriptionIntentStatus.Failed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public SubscriptionDetails ToSubscriptionDetails()
    {
        if (Status != SubscriptionIntentStatus.Succeeded ||
            ProviderSubscriptionId is null || PlanName is null || PlanHandle is null)
        {
            throw new InvalidOperationException("The subscription intent has no completed result.");
        }

        return new SubscriptionDetails(
            ProviderSubscriptionId.Value,
            ProviderReference,
            PlanName,
            PlanHandle,
            PriceInCents,
            ProviderState,
            NextBillingAt);
    }
}
