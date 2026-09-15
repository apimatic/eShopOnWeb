using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Base request that carries the authenticated caller's identity, resolved server-side from the JWT
/// (never from the request body). Endpoints call <see cref="SetCaller"/> in their route delegate
/// before handling, so the caller cannot spoof identity through the payload.
/// </summary>
public abstract class AuthenticatedRequest : BaseRequest
{
    /// <summary>The caller's username (matches <c>Order.BuyerId</c> / <c>ContactNumber.BuyerId</c>).</summary>
    public string CallerUserName { get; private set; } = string.Empty;

    /// <summary>Whether the caller holds the administrator (operator) role.</summary>
    public bool CallerIsAdmin { get; private set; }

    public void SetCaller(ClaimsPrincipal user)
    {
        CallerUserName = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        CallerIsAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
    }
}
