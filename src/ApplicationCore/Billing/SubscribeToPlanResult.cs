namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>The outcome of a subscribe request.</summary>
/// <param name="Subscription">The shopper's enrolment.</param>
/// <param name="Created">
/// True when this call created the enrolment; false when the shopper was already subscribed and
/// the existing enrolment was returned instead.
/// </param>
public sealed record SubscribeToPlanResult(CustomerSubscription Subscription, bool Created);
