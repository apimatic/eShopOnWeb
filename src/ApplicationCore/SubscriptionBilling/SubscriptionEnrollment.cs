namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// Result of enrolling a user into a plan.
/// </summary>
public sealed record SubscriptionEnrollment
{
    public required SubscriptionSummary Subscription { get; init; }

    /// <summary>The customer ID in the billing system of record.</summary>
    public int BillingCustomerId { get; init; }

    /// <summary>
    /// The eShopOnWeb user ID, stored as the customer "reference" in the billing
    /// system of record. This is the durable user-to-billing-customer mapping.
    /// </summary>
    public string BillingCustomerReference { get; init; } = string.Empty;

    /// <summary>
    /// True when the user already had a live subscription on the plan, so no new
    /// subscription was created (double-click / retry safety).
    /// </summary>
    public bool AlreadySubscribed { get; init; }
}
