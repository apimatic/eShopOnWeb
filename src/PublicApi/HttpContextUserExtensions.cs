using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi;

public static class HttpContextUserExtensions
{
    public static string? GetBuyerId(this HttpContext httpContext)
        => httpContext.User.Identity?.Name;

    public static bool IsAdministrator(this HttpContext httpContext)
        => httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

    public static IResult? RequireBuyerId(this HttpContext httpContext, out string buyerId)
    {
        buyerId = httpContext.GetBuyerId() ?? string.Empty;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        return null;
    }
}
