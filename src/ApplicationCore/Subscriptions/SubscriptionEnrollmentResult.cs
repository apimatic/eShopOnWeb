namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The outcome of a subscribe request: the resulting enrollment, plus whether it was newly
/// created or an existing live subscription for the same plan was returned instead (idempotent replay).
/// </summary>
public record SubscriptionEnrollmentResult(CustomerSubscription Subscription, bool AlreadyEnrolled);
