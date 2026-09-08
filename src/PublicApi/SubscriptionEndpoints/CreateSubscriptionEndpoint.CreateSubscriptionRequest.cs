namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (e.g. "eshop-pro"). Required.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional client-supplied idempotency key. When present it is sent to Maxio as a
    /// uniqueness_token so that retrying the same logical subscribe cannot double-subscribe.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Optional billing first name. Defaults to a name derived from the user's account.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional billing last name. Defaults to a name derived from the user's account.</summary>
    public string? LastName { get; set; }
}
