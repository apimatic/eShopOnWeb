namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The outcome of a subscribe request. The operation is idempotent: if the user already
/// has a live subscription to the requested plan, the existing one is returned and
/// <see cref="AlreadySubscribed"/> is <c>true</c> — no duplicate is created.
/// </summary>
public sealed class SubscribeResult
{
    public required CustomerSubscription Subscription { get; init; }

    /// <summary>
    /// <c>true</c> when a live subscription to the plan already existed and was returned
    /// as-is; <c>false</c> when a new subscription was created by this request.
    /// </summary>
    public bool AlreadySubscribed { get; init; }

    /// <summary>
    /// <c>true</c> when a new Maxio customer record was created for the user as part of
    /// this request; <c>false</c> when an existing customer was reused.
    /// </summary>
    public bool CustomerCreated { get; init; }
}
