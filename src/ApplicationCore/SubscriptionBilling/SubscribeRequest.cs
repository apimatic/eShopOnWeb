namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// Intent to subscribe an authenticated eShopOnWeb shopper to a plan.
/// </summary>
/// <param name="UserReference">Stable application identity of the shopper (the account email, which is the eShopOnWeb username).</param>
/// <param name="Email">Shopper email, used when provisioning the billing customer.</param>
/// <param name="FirstName">Optional given name for the billing customer.</param>
/// <param name="LastName">Optional family name for the billing customer.</param>
/// <param name="PlanHandle">Handle of the plan to subscribe to.</param>
/// <param name="IdempotencyKey">Optional caller-supplied key to make retried signups safe.</param>
public record SubscribeRequest(
    string UserReference,
    string Email,
    string? FirstName,
    string? LastName,
    string PlanHandle,
    string? IdempotencyKey = null);
