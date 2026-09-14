using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The signed-in shopper is identified by the identity claims in the JWT. The eShopOnWeb
/// token carries the account name (username) as the standard Name claim; for this sample
/// the username is the shopper's email and is used as the stable Maxio customer reference.
/// </summary>
public static class SubscriberKey
{
    public static string? From(ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return null;
        }

        var nameClaim = principal.FindFirst(ClaimTypes.Name)?.Value;
        if (!string.IsNullOrWhiteSpace(nameClaim))
        {
            return nameClaim;
        }

        var identityName = principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(identityName) ? null : identityName;
    }
}
