using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointSupport
{
    /// <summary>The caller's identity comes only from the JWT; it is never taken from the request body.</summary>
    public static string? UserNameOf(ClaimsPrincipal user) => user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;

    public static IResult Failure(SubscriptionException ex) =>
        Results.Json(new { statusCode = ex.StatusCode, message = ex.Message }, statusCode: ex.StatusCode);
}
