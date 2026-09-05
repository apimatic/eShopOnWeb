namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Client-supplied request body. Deliberately carries no buyer identity - the buyer always
/// comes from the caller's JWT, never from the request payload.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; init; } = default!;
}
