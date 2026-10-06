using System;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps the authenticated eShopOnWeb user to the billing profile handed to
/// <see cref="ISubscriptionService"/>. The user id is the stable billing reference.
/// </summary>
public static class SubscriberProfiles
{
    public static SubscriberProfile FromUser(ApplicationUser user)
    {
        var email = !string.IsNullOrWhiteSpace(user.Email)
            ? user.Email!
            : (user.UserName ?? $"user-{user.Id}@unknown.local");
        var (firstName, lastName) = DeriveName(email);
        return new SubscriberProfile(user.Id, email, firstName, lastName);
    }

    /// <summary>
    /// Billing systems require first/last names; eShopOnWeb users only carry an email,
    /// so the name is derived deterministically from its local part.
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var localPart = email;
        var at = email.IndexOf('@');
        if (at > 0)
        {
            localPart = email[..at];
        }
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstName = parts.Length > 0 ? Capitalize(parts[0]) : "eShop";
        var lastName = parts.Length > 1 ? Capitalize(string.Join(" ", parts[1..])) : "Customer";
        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
}