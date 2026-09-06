using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request.
/// </summary>
public class SubscribeResult
{
    private SubscribeResult(Subscription subscription, bool created)
    {
        Subscription = Guard.Against.Null(subscription, nameof(subscription));
        Created = created;
    }

    public Subscription Subscription { get; }

    /// <summary>
    /// False when the shopper already had a live subscription to the requested plan and this call
    /// returned it unchanged. Subscribing is idempotent, so a repeated (or double-clicked) request
    /// reports <c>false</c> rather than creating a second subscription.
    /// </summary>
    public bool Created { get; }

    public static SubscribeResult Subscribed(Subscription subscription) => new(subscription, created: true);

    public static SubscribeResult AlreadySubscribed(Subscription subscription) => new(subscription, created: false);
}
