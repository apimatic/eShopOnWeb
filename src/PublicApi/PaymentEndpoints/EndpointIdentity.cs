using System;
using System.Security.Claims;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class EndpointIdentity
{
    public static string GetBuyerId(ClaimsPrincipal user) =>
        user.Identity?.Name
        ?? user.FindFirst(ClaimTypes.Name)?.Value
        ?? throw new UnauthorizedAccessException("The authenticated caller has no user name.");
}
