using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Extensions;

public static class HttpUserExtensions
{
    public static string? GetBuyerId(this ClaimsPrincipal user)
        => user.Identity?.Name;

    public static bool IsAdministrator(this ClaimsPrincipal user)
        => user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
