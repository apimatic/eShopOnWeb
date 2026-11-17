using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class EndpointUser
{
    public static string RequireUserName(HttpContext context)
    {
        var name = context.User.FindFirst(ClaimTypes.Name)?.Value
            ?? context.User.Identity?.Name;

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentForbiddenException("A signed-in user is required.");
        }

        return name!;
    }

    public static bool IsAdministrator(HttpContext context) =>
        context.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)
        || context.User.HasClaim(
            ClaimTypes.Role,
            BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
