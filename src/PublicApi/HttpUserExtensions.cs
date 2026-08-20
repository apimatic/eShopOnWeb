using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string? GetBuyerId(this HttpContext httpContext) =>
        httpContext.User.Identity?.Name;

    public static bool IsAdministrator(this HttpContext httpContext) =>
        httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

    public static IResult? UnauthorizedIfAnonymous(this HttpContext httpContext)
    {
        if (string.IsNullOrEmpty(httpContext.User.Identity?.Name))
        {
            return Results.Unauthorized();
        }

        return null;
    }
}
