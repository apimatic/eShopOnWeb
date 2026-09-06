namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Everything needed to enroll an authenticated eShopOnWeb user onto a plan.
/// </summary>
/// <param name="Subscriber">The authenticated user, as resolved from the caller's token.</param>
/// <param name="PlanHandle">Handle of the plan to subscribe to.</param>
public sealed record SubscribeRequest(SubscriberIdentity Subscriber, string PlanHandle);

/// <summary>
/// The identity a subscription is created for. <paramref name="UserKey"/> is the value the billing
/// customer reference is derived from, so it must be stable for the lifetime of the account.
/// </summary>
/// <param name="UserKey">Stable key of the eShopOnWeb user (its user name).</param>
/// <param name="Email">Email to register with the billing provider.</param>
/// <param name="FirstName">Optional first name; derived from the email when omitted.</param>
/// <param name="LastName">Optional last name; derived from the email when omitted.</param>
/// <param name="Organization">Optional organization/company name.</param>
public sealed record SubscriberIdentity(
    string UserKey,
    string Email,
    string? FirstName = null,
    string? LastName = null,
    string? Organization = null);

/// <summary>
/// Outcome of a subscribe attempt.
/// </summary>
/// <param name="Subscription">The live subscription the user now holds.</param>
/// <param name="AlreadyExisted">
/// True when the user was already enrolled on this plan and no new subscription was created.
/// This is what makes a double-clicked subscribe safe.
/// </param>
public sealed record SubscribeResult(CustomerSubscription Subscription, bool AlreadyExisted);
