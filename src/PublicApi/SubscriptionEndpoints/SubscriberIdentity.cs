using System;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Derives the Maxio customer identity from the authenticated caller. The eShop user's
/// identity (the JWT <c>ClaimTypes.Name</c>, which is the account email) is used as the Maxio
/// customer <c>reference</c> — the stable, unique key that makes customer creation idempotent.
/// </summary>
internal static class SubscriberIdentity
{
    public static string RequireUserReference(ClaimsPrincipal user)
    {
        var reference = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(reference))
        {
            reference = user.FindFirst(ClaimTypes.Name)?.Value;
        }

        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new InvalidOperationException("An authenticated user identity is required to subscribe.");
        }

        return reference;
    }

    public static MaxioCustomerInput ToCustomerInput(string reference)
    {
        var local = reference.Contains('@') ? reference.Substring(0, reference.IndexOf('@', StringComparison.Ordinal)) : reference;
        return new MaxioCustomerInput
        {
            Reference = reference,
            Email = reference,
            FirstName = string.IsNullOrWhiteSpace(local) ? "eShop" : local,
            LastName = "Customer"
        };
    }
}
