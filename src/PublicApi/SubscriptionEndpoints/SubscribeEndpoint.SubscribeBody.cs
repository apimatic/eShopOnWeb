namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>The JSON body shape for <see cref="SubscribeEndpoint"/> - the buyer is taken from the JWT, not the body.</summary>
public class SubscribeBody
{
    public string ProductHandle { get; set; } = string.Empty;
}
