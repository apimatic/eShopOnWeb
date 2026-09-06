namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of <c>POST /api/subscriptions</c>. The subscriber is never taken from the body &#8212; it
/// comes from the bearer token.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to. Optional only when <c>Maxio:DefaultPlanHandle</c> is
    /// configured, in which case that plan is used.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>Optional price point handle, for plans that expose more than one.</summary>
    public string? PricePointHandle { get; set; }

    /// <summary>
    /// Optional idempotency key. May also be sent as the <c>Idempotency-Key</c> header; the header
    /// wins when both are present. Replaying the same key returns the original subscription
    /// instead of creating a second one.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Optional given name for the billing customer record. Derived from the email when omitted.
    /// Only used the first time a customer record is created for this shopper.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// Optional family name for the billing customer record. Derived from the email when omitted.
    /// Only used the first time a customer record is created for this shopper.
    /// </summary>
    public string? LastName { get; set; }
}
