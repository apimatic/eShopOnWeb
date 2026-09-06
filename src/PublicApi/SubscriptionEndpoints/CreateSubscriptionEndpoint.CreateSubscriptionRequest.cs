namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A request to enrol the authenticated caller in a plan.
/// </summary>
/// <remarks>
/// There is deliberately no user field: the shopper is taken from the bearer token, so a caller
/// cannot subscribe anybody but themselves.
/// </remarks>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of a plan from <c>GET /api/subscription-plans</c>, e.g. <c>pro-plan</c>. Required.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Given name to store on the billing customer record. Optional; when omitted it is derived
    /// from the caller's identity. Only used the first time a billing record is created.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// Family name to store on the billing customer record. Optional; when omitted it is derived
    /// from the caller's identity. Only used the first time a billing record is created.
    /// </summary>
    public string? LastName { get; set; }

    /// <summary>
    /// Company name to store on the billing customer record. Optional. Only used the first time a
    /// billing record is created.
    /// </summary>
    public string? Organization { get; set; }

    /// <summary>
    /// Overrides the configured payment collection method for this subscription, e.g.
    /// <c>automatic</c> to charge a stored payment method instead of invoicing. Optional.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}
