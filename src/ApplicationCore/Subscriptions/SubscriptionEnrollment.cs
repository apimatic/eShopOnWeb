namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The outcome of a subscribe request.
/// <see cref="AlreadyEnrolled"/> is true when an equivalent live subscription already existed,
/// which is what makes a double-clicked subscribe safe: the same enrollment comes back twice.
/// </summary>
public record SubscriptionEnrollment
{
    public required CustomerSubscription Subscription { get; init; }
    public required BillingCustomer Customer { get; init; }
    public required bool AlreadyEnrolled { get; init; }
    public bool CustomerAlreadyExisted { get; init; }
}
