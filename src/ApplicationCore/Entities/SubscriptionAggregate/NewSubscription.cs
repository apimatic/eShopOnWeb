namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>Subscription to create for an existing billing customer.</summary>
public class NewSubscription
{
    public required int CustomerId { get; init; }

    public required string PlanHandle { get; init; }

    /// <summary>Maxio collection method, e.g. "remittance" or "automatic".</summary>
    public required string PaymentCollectionMethod { get; init; }

    /// <summary>
    /// Optional unique reference. Maxio enforces uniqueness site-wide, which makes it a
    /// server-side idempotency guard for concurrent subscribe attempts.
    /// </summary>
    public string? Reference { get; init; }
}
