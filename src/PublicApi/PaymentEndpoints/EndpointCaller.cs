using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Resolves the caller's identity (the JWT <see cref="ClaimTypes.Name"/> claim, which is the buyer id)
/// from the current request. Shopper-scoped endpoints use this to act only on the caller's own data.
/// </summary>
/// <summary>Marker request for endpoints that take neither a body nor a route id.</summary>
public record EmptyRequest();

public static class EndpointCaller
{
    public static string RequireBuyerId(IHttpContextAccessor accessor)
    {
        var name = accessor.HttpContext?.User?.Identity?.Name
                   ?? accessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(name)
            ? throw new System.UnauthorizedAccessException("The caller's identity could not be determined.")
            : name;
    }
}
