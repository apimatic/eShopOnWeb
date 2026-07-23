using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared plumbing for the authenticated subscription endpoints: it resolves which eShopOnWeb
/// user the request applies to, honouring the administrator override.
/// </summary>
public abstract class SubscriptionEndpointBase
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    protected SubscriptionEndpointBase(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected ClaimsPrincipal? CurrentUser => _httpContextAccessor.HttpContext?.User;

    /// <summary>
    /// Denies the request with an explicit 403. <c>Results.Forbid()</c> is deliberately avoided:
    /// this host also has Identity's cookie scheme registered, which turns a forbid into a 302
    /// redirect to a login page — useless to an API client.
    /// </summary>
    protected static IResult Denied() => Results.StatusCode(StatusCodes.Status403Forbidden);

    /// <summary>
    /// Resolves the target user, or <c>null</c> when the caller may not act on the user they
    /// asked for. Endpoints translate <c>null</c> into a 403.
    /// </summary>
    protected string? ResolveUserReference(string? requestedUserReference)
    {
        var caller = CurrentUser;

        return caller is null ? null : SubscriptionCaller.ResolveUserReference(caller, requestedUserReference);
    }

    /// <summary>
    /// Reads the optional administrator override from the query string, for endpoints that carry
    /// no request body.
    /// </summary>
    protected string? RequestedUserReferenceFromQuery()
    {
        var query = _httpContextAccessor.HttpContext?.Request.Query;

        return query is not null && query.TryGetValue("userReference", out var value)
            ? value.ToString()
            : null;
    }
}
