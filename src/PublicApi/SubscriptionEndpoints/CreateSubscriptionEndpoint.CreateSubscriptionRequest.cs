namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of <c>POST /api/subscriptions</c>. The subscriber is taken from the bearer token, never
/// from this payload.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>.
    /// Optional only when the deployment configures <c>Maxio:DefaultPlanHandle</c>.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional client-supplied idempotency key. Repeating a request with the same key enrolls the
    /// shopper at most once, even when the first call's outcome was never observed.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Optional given name for the billing customer record, used only the first time an account is
    /// enrolled. Defaults to a value derived from the account's login.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// Optional family name for the billing customer record, used only the first time an account is
    /// enrolled. Defaults to a value derived from the account's login.
    /// </summary>
    public string? LastName { get; set; }
}
