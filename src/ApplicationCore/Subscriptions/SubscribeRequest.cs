namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Input to <see cref="Interfaces.ISubscriptionBillingService.SubscribeAsync"/>. The identity
/// fields come from the authenticated caller's token, never from the request body.
/// </summary>
/// <param name="UserReference">
/// Stable per-user identifier used as the billing customer's reference (idempotency key for the
/// customer). In eShopOnWeb this is the authenticated user name.
/// </param>
/// <param name="Email">The customer's email address.</param>
/// <param name="FirstName">The customer's first name.</param>
/// <param name="LastName">The customer's last name.</param>
/// <param name="PlanHandle">
/// Handle of the plan to subscribe to. When null, the service subscribes to its configured
/// default plan (falling back to the first available plan).
/// </param>
public record SubscribeRequest(
    string UserReference,
    string Email,
    string FirstName,
    string LastName,
    string? PlanHandle);
