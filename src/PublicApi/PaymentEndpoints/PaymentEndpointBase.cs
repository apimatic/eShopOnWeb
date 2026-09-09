using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Shared helpers for the payment endpoints: the caller's identity (the JWT <c>name</c> claim, which
/// is the same value used as <c>Order.BuyerId</c>) and the request-aborted cancellation token, so
/// every SDK call downstream is bounded by the HTTP request's lifetime.
/// </summary>
public abstract class PaymentEndpointBase
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    protected PaymentEndpointBase(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>The signed-in shopper's identity. Endpoints are <c>[Authorize]</c>d, so this is present.</summary>
    protected string BuyerId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var name = user?.FindFirstValue(ClaimTypes.Name) ?? user?.Identity?.Name;
            return string.IsNullOrEmpty(name)
                ? throw new System.UnauthorizedAccessException("The caller identity could not be determined from the token.")
                : name;
        }
    }

    protected CancellationToken RequestAborted =>
        _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
}
