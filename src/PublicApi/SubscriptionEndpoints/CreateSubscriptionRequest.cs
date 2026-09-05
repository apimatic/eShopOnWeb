namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to (see GET /api/subscription-plans).</summary>
    public required string PlanHandle { get; init; }

    /// <summary>Optional; if omitted, a name is derived from the caller's account email.</summary>
    public string? FirstName { get; init; }

    /// <summary>Optional; if omitted, a name is derived from the caller's account email.</summary>
    public string? LastName { get; init; }

    /// <summary>
    /// Server-assigned from the caller's JWT identity after model binding - any value a client
    /// sends for these two properties is discarded before the request is handled.
    /// </summary>
    public string CallerReference { get; set; } = string.Empty;
    public string CallerEmail { get; set; } = string.Empty;
}
