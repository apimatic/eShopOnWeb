using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    /// <summary>Units consumed. Must be greater than zero.</summary>
    public int Quantity { get; set; }

    /// <summary>An optional note stored alongside the usage.</summary>
    public string Memo { get; set; }

    internal int SubscriptionId { get; private set; }

    /// <summary>
    /// The user whose subscriptions the caller may act on; <c>null</c> for an administrator.
    /// Derived from the bearer token, never from the body.
    /// </summary>
    internal string ActingUserReference { get; private set; }

    internal CancellationToken CancellationToken { get; private set; }

    internal void Bind(int subscriptionId, string actingUserReference, CancellationToken cancellationToken)
    {
        SubscriptionId = subscriptionId;
        ActingUserReference = actingUserReference;
        CancellationToken = cancellationToken;
    }
}
