using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Services;

/// <summary>
/// Accessor for the identity of the currently authenticated caller (JWT claims).
/// The identity claim (username - an email in this app) doubles as the stable Maxio
/// customer reference, which is what makes subscription provisioning idempotent
/// across restarts and between hosts.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// The eShopOnWeb account name bound to the access token. Null when the token
    /// carries no identity claim.
    /// </summary>
    string? GetUserReference();
}

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? GetUserReference()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user is null)
            return null;

        return user.Identity?.Name
            ?? user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.FindFirst("unique_name")?.Value;
    }
}
