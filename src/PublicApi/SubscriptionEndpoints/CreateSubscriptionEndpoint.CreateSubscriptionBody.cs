namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The subscribe request body: the plan to subscribe to, identified by its Maxio product handle.
/// </summary>
public class CreateSubscriptionBody
{
    public string ProductHandle { get; set; } = string.Empty;
}