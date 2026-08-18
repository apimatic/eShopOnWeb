using System;
using System.Threading;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Shared helpers for the SMS-notification endpoints: the caller's identity (from the JWT) and the
/// request's cancellation token. Uses <see cref="IHttpContextAccessor"/> (a singleton, so it is safe
/// to inject into these endpoints, which are constructed once at startup).
/// </summary>
public abstract class ApiEndpointBase
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    protected ApiEndpointBase(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected HttpContext HttpContext =>
        _httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No active HttpContext.");

    /// <summary>The signed-in caller's identity (username) taken from the token, or null if unauthenticated.</summary>
    protected string? CallerId => HttpContext.User?.Identity?.Name;

    protected CancellationToken Aborted => HttpContext.RequestAborted;
}
