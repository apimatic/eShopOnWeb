namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Input to <see cref="Interfaces.IMaxioBillingService.SubscribeAsync"/>. Carries the
/// authenticated shopper's identity (used as the idempotent customer reference) and the
/// plan they chose.
/// </summary>
public class SubscribeRequest
{
    /// <summary>
    /// Stable, unique reference for the shopper — the eShopOnWeb user name. Used verbatim
    /// as the Maxio customer <c>reference</c> so repeated calls map to one customer.
    /// </summary>
    public string UserReference { get; init; } = string.Empty;

    /// <summary>Shopper email; also used to locate an existing Maxio customer.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Optional given name for a newly created customer.</summary>
    public string? FirstName { get; init; }

    /// <summary>Optional family name for a newly created customer.</summary>
    public string? LastName { get; init; }

    /// <summary>
    /// Handle of the plan to enroll in. When null/empty the service falls back to the
    /// configured default product family's default plan.
    /// </summary>
    public string? PlanHandle { get; init; }
}
