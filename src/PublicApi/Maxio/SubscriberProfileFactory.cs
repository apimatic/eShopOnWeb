using System;
using System.Linq;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Builds the Maxio customer profile for a bearer-token identity. The Maxio customer reference is the
/// ASP.NET user id claim, so a customer is found idempotently on every request for that user. First and
/// last name come from the caller when provided and otherwise are derived deterministically from the
/// email, because this application stores no separate user profile.
/// </summary>
public static class SubscriberProfileFactory
{
    public static SubscriberProfile? Create(ClaimsPrincipal? user, string? firstName, string? lastName)
    {
        if (user is null)
        {
            return null;
        }

        var reference = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var trimmedEmail = email.Trim();
        var (resolvedFirstName, resolvedLastName) = ResolveNames(trimmedEmail, firstName, lastName);
        return new SubscriberProfile(reference.Trim(), trimmedEmail, resolvedFirstName, resolvedLastName);
    }

    private static (string FirstName, string LastName) ResolveNames(string email, string? firstName, string? lastName)
    {
        var requestedFirstName = Clean(firstName);
        var requestedLastName = Clean(lastName);
        if (requestedFirstName is not null && requestedLastName is not null)
        {
            return (requestedFirstName, requestedLastName);
        }

        var localPart = email.Split('@', 2)[0];
        var segments = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Capitalize)
            .ToArray();
        if (segments.Length == 0)
        {
            return (requestedFirstName ?? "Valued", requestedLastName ?? "Customer");
        }

        if (segments.Length == 1)
        {
            return (requestedFirstName ?? segments[0], requestedLastName ?? segments[0]);
        }

        return (requestedFirstName ?? segments[0], requestedLastName ?? string.Join(" ", segments.Skip(1)));
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value.Substring(1);
    }
}
