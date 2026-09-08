using System.Security.Claims;
using BlazorShared.Models;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Shared helpers for the subscription endpoints.</summary>
internal static class SubscriptionEndpointHelpers
{
    /// <summary>
    /// Returns the authenticated caller's identifier (the JWT name claim, which eShopOnWeb sets to the
    /// user's email/user name). Returns <c>null</c> when the principal carries no name claim.
    /// </summary>
    public static string? GetUserEmail(ClaimsPrincipal user)
    {
        string? name = user.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>Builds an error result whose body matches the ErrorDetails shape used elsewhere by PublicApi.</summary>
    public static ObjectResult Error(int statusCode, string message)
    {
        return new ObjectResult(new ErrorDetails { StatusCode = statusCode, Message = message })
        {
            StatusCode = statusCode
        };
    }
}
