namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>.
    /// Required.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional key that pins repeated submissions to a single subscription. Omit it and the plan
    /// handle scopes idempotency instead, which already makes a double-clicked Subscribe button safe;
    /// supply one when the client wants to retry a request without any chance of a second enrolment.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Optional given name to record on the billing customer. eShopOnWeb accounts carry no name, so
    /// without this a placeholder is derived from the email address.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>Optional family name to record on the billing customer.</summary>
    public string? LastName { get; set; }
}
