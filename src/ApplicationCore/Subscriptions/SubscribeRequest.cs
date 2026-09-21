namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Input to <see cref="Interfaces.ISubscriptionBillingService.SubscribeAsync"/>. The identity fields
/// come from the authenticated caller; the plan handle is what the shopper chose.
/// </summary>
public record SubscribeRequest
{
    /// <summary>
    /// Stable identifier for the eShop user, used as the billing customer's <c>reference</c>. This is the
    /// durable link between an eShop user and their Maxio customer, so it must be stable across restarts.
    /// </summary>
    public required string UserReference { get; init; }

    public required string Email { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    /// <summary>The API handle of the plan (product) to subscribe to.</summary>
    public required string PlanHandle { get; init; }
}

/// <summary>Result of a subscribe call: the subscription, and whether it was newly created (vs. an idempotent reuse).</summary>
public record SubscribeOutcome(CustomerSubscription Subscription, bool WasCreated);
