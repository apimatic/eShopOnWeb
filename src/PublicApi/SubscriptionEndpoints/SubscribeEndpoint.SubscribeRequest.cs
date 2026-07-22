using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>The handle of the plan to enrol in, e.g. <c>eshop-pro</c>.</summary>
    public string PlanHandle { get; set; }

    /// <summary>
    /// The enrolling user. Populated from the bearer token by the endpoint — anything a client
    /// sends here is discarded.
    /// </summary>
    internal string UserReference { get; private set; }

    internal CancellationToken CancellationToken { get; private set; }

    internal void Bind(string userReference, CancellationToken cancellationToken)
    {
        UserReference = userReference;
        CancellationToken = cancellationToken;
    }
}
