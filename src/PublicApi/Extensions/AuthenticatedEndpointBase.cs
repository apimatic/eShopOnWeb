using System.Threading;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Extensions;

/// <summary>
/// Base for endpoints that act as the signed-in caller. Provides the caller's BuyerId and the request's
/// cancellation token from the ambient <see cref="HttpContext"/> — obtained via a singleton
/// <see cref="IHttpContextAccessor"/> so it is safe regardless of the endpoint's own lifetime, while the
/// per-request application service is resolved through the route delegate.
/// </summary>
public abstract class AuthenticatedEndpointBase
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    protected AuthenticatedEndpointBase(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected string BuyerId => _httpContextAccessor.HttpContext!.User.GetBuyerId();

    protected CancellationToken RequestAborted => _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
}
