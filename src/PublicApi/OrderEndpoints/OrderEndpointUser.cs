using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class OrderEndpointUser
{
    /// <summary>The caller's identity as carried by the JWT; orders are owned by this name (the buyer id).</summary>
    public static string RequireName(ClaimsPrincipal user)
    {
        var name = user.Identity?.IsAuthenticated == true ? user.Identity.Name : null;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BadHttpRequestException("The access token does not identify a user.", StatusCodes.Status401Unauthorized);
        }
        return name;
    }
}
