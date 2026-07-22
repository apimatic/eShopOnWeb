using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public MySubscriptionsRequest(string? userName, CancellationToken cancellationToken = default)
    {
        UserName = userName;
        CancellationToken = cancellationToken;
    }

    /// <summary>The authenticated caller's identity, taken from the bearer token.</summary>
    public string? UserName { get; init; }

    public CancellationToken CancellationToken { get; init; }
}
