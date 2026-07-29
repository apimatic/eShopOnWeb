namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The intent to enroll an eShopOnWeb user into a plan. The caller's identity
/// (<see cref="UserReference"/> / <see cref="Email"/>) is derived from the authenticated
/// principal, never from client-supplied data, so a user can only subscribe themselves.
/// </summary>
public class SubscribeRequest
{
    /// <summary>
    /// Stable, unique identifier of the eShopOnWeb user. Used as the Maxio customer
    /// <c>reference</c> so the mapping is idempotent across requests and restarts.
    /// </summary>
    public string UserReference { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    /// <summary>Handle of the plan (Maxio product) to subscribe to, e.g. <c>eshop-pro</c>.</summary>
    public string PlanHandle { get; init; } = string.Empty;
}
