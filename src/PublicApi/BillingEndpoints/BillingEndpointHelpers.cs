using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

internal static class BillingEndpointHelpers
{
    /// <summary>The caller's identity comes from the JWT (name claim).</summary>
    public static string CallerName(ClaimsPrincipal principal)
    {
        var name = principal.FindFirst(ClaimTypes.Name)?.Value ?? principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new BillingException(System.Net.HttpStatusCode.Unauthorized, "The token does not identify a user.");
        return name;
    }
}
