using System;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Shared plumbing for the subscription endpoints: caller identity and error responses.</summary>
public static class BillingEndpointSupport
{
    public const string Tag = "SubscriptionEndpoints";

    /// <summary>The caller's user name from the JWT (<see cref="ClaimTypes.Name"/>); null when absent.</summary>
    public static string? UserNameOf(ClaimsPrincipal user)
    {
        var name = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    /// Converts a <see cref="BillingException"/> into an RFC 7807 problem response. Only the caller-safe
    /// message, the request correlation id and Maxio's own validation <c>errors</c> strings are returned.
    /// </summary>
    public static IResult Problem(BillingException ex, Guid correlationId)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["correlationId"] = correlationId
        };
        if (ex.Errors.Count > 0)
        {
            extensions["errors"] = ex.Errors;
        }

        return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode, title: ex.Title, extensions: extensions);
    }
}
