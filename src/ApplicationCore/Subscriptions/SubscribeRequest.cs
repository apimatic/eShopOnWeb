namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A request to enroll a <see cref="Subscriber"/> on a plan.
/// </summary>
public class SubscribeRequest
{
    /// <summary>
    /// Handle of the plan to enroll on. When null the configured default plan handle is used;
    /// if no default is configured the request is rejected.
    /// </summary>
    public string? PlanHandle { get; init; }

    /// <summary>
    /// Caller-supplied idempotency key. Two calls carrying the same key for the same subscriber
    /// are guaranteed to enroll at most once, even if the first call's outcome was never observed.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Optional given name to open the billing customer record with.</summary>
    public string? FirstName { get; init; }

    /// <summary>Optional family name to open the billing customer record with.</summary>
    public string? LastName { get; init; }
}
