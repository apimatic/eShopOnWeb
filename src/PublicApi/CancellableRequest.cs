using System.Text.Json.Serialization;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Base class for requests served by minimal-API endpoints that need the request's
/// cancellation token carried from the route lambda into the endpoint. Kept off
/// <see cref="BaseRequest"/> because MVC treats a CancellationToken member on a model as a
/// special binding source, which would break body binding on the controller-style endpoints.
/// </summary>
public abstract class CancellableRequest : BaseRequest
{
    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
}
