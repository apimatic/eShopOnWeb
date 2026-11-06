using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionUserExtensions
{
    /// <summary>The signed-in shopper's identity, taken from the JWT.</summary>
    public static string ShopperId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name ?? user.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BillingException(401, "The token carries no user identity.");
        }
        return name;
    }
}
