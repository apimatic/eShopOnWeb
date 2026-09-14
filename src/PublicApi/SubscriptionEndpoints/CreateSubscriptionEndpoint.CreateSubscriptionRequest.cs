namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The JSON body accepted by POST api/subscriptions.
/// </summary>
public class SubscribeToPlanBody
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionRequest : BaseRequest
{
    public CreateSubscriptionRequest(string planHandle, string customerEmail)
    {
        PlanHandle = planHandle;
        CustomerEmail = customerEmail;
    }

    /// <summary>API handle of the plan to subscribe to, e.g. "eshop-pro".</summary>
    public string PlanHandle { get; }

    /// <summary>The authenticated caller's identity, taken from the JWT - never from the request body.</summary>
    public string CustomerEmail { get; }
}
