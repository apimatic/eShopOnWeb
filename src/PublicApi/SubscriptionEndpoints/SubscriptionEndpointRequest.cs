using System.Text.Json.Serialization;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Base for the subscription requests. The endpoint contract fixes <c>HandleAsync</c> to a request
/// and a service, so the ambient values a billing call needs - the abort token and the
/// authenticated subscriber - travel on the request. Both are set on the server and neither is
/// bound from the request body.
/// </summary>
public abstract class SubscriptionEndpointRequest : BaseRequest
{
    /// <summary>Signals that the caller went away, so in-flight provider calls can be abandoned.</summary>
    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
}
