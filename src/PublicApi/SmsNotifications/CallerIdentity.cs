using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

/// <summary>
/// Reads the caller's identity out of the bearer token. The username in <see cref="ClaimTypes.Name"/>
/// is what an <see cref="ApplicationCore.Entities.OrderAggregate.Order"/> and a
/// <see cref="ApplicationCore.Entities.ContactNumberAggregate.ContactNumber"/> are scoped by, so
/// shopper-facing endpoints act only on the caller's own data.
/// </summary>
public static class CallerIdentity
{
    public static string? GetBuyerId(HttpContext httpContext) =>
        httpContext.User.FindFirstValue(ClaimTypes.Name);
}
