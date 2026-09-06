namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A request to enroll one eShopOnWeb user in one plan.
/// </summary>
/// <param name="Subscriber">The eShopOnWeb user placing the request.</param>
/// <param name="PlanHandle">Handle of the plan to subscribe to. Required unless a default plan is configured.</param>
/// <param name="IdempotencyKey">
/// Optional caller-supplied key. Two requests from the same subscriber carrying the same key
/// produce at most one subscription - across processes, and even after the plan has been
/// cancelled and legitimately re-taken. A key is not required for safety: a subscriber is never
/// enrolled twice in a plan they already hold, keyed or not.
/// </param>
public record SubscribeRequest(Subscriber Subscriber, string? PlanHandle, string? IdempotencyKey = null);

/// <summary>
/// The eShopOnWeb user a billing customer is created for.
/// </summary>
/// <param name="Key">
/// Stable application-side identity of the user. It is used verbatim to derive the billing
/// customer reference, so it must not change over the user's lifetime.
/// </param>
public record Subscriber(string Key, string Email, string? FirstName = null, string? LastName = null);
