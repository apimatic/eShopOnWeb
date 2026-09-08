using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Application-level service that talks to Maxio Advanced Billing (the billing system of record)
/// and encapsulates the subscription catalog, the customer lifecycle and subscription management.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>Lists the subscribable plans (products) in the configured Maxio product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>Returns the site currency (e.g. USD) used for display purposes.</summary>
    Task<string?> GetSiteCurrencyAsync(CancellationToken cancellationToken);

    /// <summary>Lists the Maxio subscriptions that belong to the customer referenced by <paramref name="customerReference"/>.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListUserSubscriptionsAsync(string customerReference, CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a Maxio customer exists for the given application user reference (creating it when needed)
    /// and subscribes that customer to the requested plan. Idempotent: when the customer already has a
    /// current subscription to the plan, the existing subscription is returned and nothing new is created.
    /// </summary>
    Task<MaxioSubscribeResult> SubscribeAsync(string customerReference, string email, string productHandle, CancellationToken cancellationToken);
}

public sealed record MaxioSubscribeResult(MaxioSubscription Subscription, bool AlreadySubscribed);
