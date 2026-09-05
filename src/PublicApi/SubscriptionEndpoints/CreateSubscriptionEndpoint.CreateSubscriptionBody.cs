namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The JSON body a caller posts to subscribe. Identity fields (customer reference/email/name) are
/// never taken from client input — see <see cref="CreateSubscriptionEndpoint"/>.
/// </summary>
public class CreateSubscriptionBody
{
    public string PlanHandle { get; set; } = string.Empty;
}
