using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// eShopOnWeb's view of a billing-provider subscription. The billing provider is the system of
/// record (§8 decision: stateless mapping, idempotent on the customer reference) so this is a
/// read model reconstructed from the provider on every call, not an EF-persisted aggregate.
/// </summary>
public class Subscription
{
    public int Id { get; }
    public int? ProviderCustomerId { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public int PriceInCents { get; }
    public SubscriptionState State { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    public Subscription(
        int id,
        int? providerCustomerId,
        string productHandle,
        string productName,
        int priceInCents,
        SubscriptionState state,
        DateTimeOffset? currentPeriodEndsAt)
    {
        Id = id;
        ProviderCustomerId = providerCustomerId;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        State = state;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
    }
}
