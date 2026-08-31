using System.Security.Claims;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>Reads the caller's identity from the JWT: their username, and whether they are an operator.</summary>
public static class CallerIdentity
{
    public static string? BuyerId(ClaimsPrincipal user) => user.Identity?.Name;

    public static bool IsOperator(ClaimsPrincipal user) => user.IsInRole(Constants.Roles.ADMINISTRATORS);
}
