using System;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds a <see cref="SubscriberIdentity"/> from the authenticated caller's JWT. The caller's
/// identity comes entirely from the token (the username claim, which is the shopper's email in
/// eShopOnWeb). The email is used as the stable billing customer reference so a single Maxio
/// customer maps to a single eShop user across requests and process restarts.
/// </summary>
internal static class SubscriberIdentityFactory
{
    /// <summary>
    /// Resolves the subscriber from the principal, or returns null when the token carries no
    /// usable identity (the endpoints require authorization, so this is a defensive guard).
    /// </summary>
    public static SubscriberIdentity? FromPrincipal(ClaimsPrincipal principal)
    {
        var username = principal.FindFirstValue(ClaimTypes.Name)
                       ?? principal.Identity?.Name
                       ?? principal.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var email = username.Contains('@', StringComparison.Ordinal)
            ? username
            : principal.FindFirstValue(ClaimTypes.Email) ?? username;

        var atIndex = email.IndexOf('@', StringComparison.Ordinal);
        var firstName = atIndex > 0 ? email[..atIndex] : email;

        return new SubscriberIdentity(
            reference: username,
            email: email,
            firstName: firstName,
            lastName: "eShopOnWeb");
    }
}
