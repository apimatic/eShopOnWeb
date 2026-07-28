namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Request to enroll an eShopOnWeb user into a subscription plan.
/// The identity fields are resolved from the authenticated caller, not supplied by the client.
/// </summary>
public class SubscribeCommand
{
    /// <summary>
    /// Stable, unique identifier for the eShopOnWeb user. Used as the Maxio customer
    /// <c>reference</c> so that repeated calls map to a single customer (idempotency key).
    /// </summary>
    public string UserReference { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    /// <summary>
    /// The Maxio product handle of the plan to subscribe to. When null/empty, the service
    /// falls back to the first available plan in the configured product family.
    /// </summary>
    public string? PlanHandle { get; init; }
}
