using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Read model of a subscription as it currently stands with the billing provider. This is a
/// provider-agnostic projection, not an EF-persisted aggregate: eShopOnWeb keeps a stateless
/// mapping to the provider (idempotent on the customer reference), so instances are built fresh
/// from <see cref="IBillingClient"/> responses rather than loaded from a repository.
/// </summary>
public class Subscription : BaseEntity, IAggregateRoot
{
    public string UserName { get; }
    public int ProviderCustomerId { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public int PriceInCents { get; }
    public SubscriptionState State { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }
    public DateTimeOffset? NextAssessmentAt { get; }

    public Subscription(int providerSubscriptionId,
        string userName,
        int providerCustomerId,
        string productHandle,
        string productName,
        int priceInCents,
        SubscriptionState state,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt)
    {
        Id = providerSubscriptionId;
        UserName = userName;
        ProviderCustomerId = providerCustomerId;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        State = state;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
    }
}
