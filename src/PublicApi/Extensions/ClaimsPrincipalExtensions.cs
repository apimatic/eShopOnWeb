using System;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Extensions;

internal static class ClaimsPrincipalExtensions
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        return user.Identity?.Name
            ?? user.FindFirst(ClaimTypes.Name)?.Value
            ?? throw new InvalidOperationException("The caller is missing an identity name claim.");
    }

    public static bool IsAdministrator(this ClaimsPrincipal user)
        => user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
