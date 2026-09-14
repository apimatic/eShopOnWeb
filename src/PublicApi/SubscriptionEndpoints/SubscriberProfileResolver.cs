using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the bearer token of the current request into the subscriber the billing system should
/// bill.
/// </summary>
/// <remarks>
/// The caller never supplies their own identity: the user name comes from the validated JWT and the
/// e-mail address is read back from ASP.NET Identity, so a token holder cannot subscribe on behalf
/// of someone else. Only cosmetic details - the name and organisation Maxio shows on an invoice -
/// may be supplied by the caller.
/// </remarks>
public interface ISubscriberProfileResolver
{
    Task<SubscriberProfile?> ResolveAsync(ClaimsPrincipal principal, SubscriberDetails? details = null);
}

/// <inheritdoc cref="ISubscriberProfileResolver"/>
public class SubscriberProfileResolver : ISubscriberProfileResolver
{
    /// <summary>Used when a shopper's e-mail address yields no usable surname.</summary>
    private const string FallbackLastName = "Customer";

    private static readonly char[] NameSeparators = { '.', '_', '-' };

    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriberProfileResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<SubscriberProfile?> ResolveAsync(ClaimsPrincipal principal, SubscriberDetails? details = null)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        // eShopOnWeb seeds users with the e-mail address as the user name, but do not assume it:
        // fall back to the user name only when no e-mail is recorded.
        var email = user.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            email = userName;
        }

        var (derivedFirstName, derivedLastName) = DeriveName(email);

        return new SubscriberProfile(
            UserId: user.Id,
            Email: email,
            FirstName: Coalesce(details?.FirstName, derivedFirstName),
            LastName: Coalesce(details?.LastName, derivedLastName),
            Organization: string.IsNullOrWhiteSpace(details?.Organization) ? null : details.Organization.Trim());
    }

    /// <summary>
    /// Derives a first and last name from an e-mail address, because eShopOnWeb identities carry no
    /// names of their own while the billing provider requires both.
    /// </summary>
    internal static (string FirstName, string LastName) DeriveName(string email)
    {
        var localPart = email.Split('@')[0].Split('+')[0];

        var tokens = localPart
            .Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Any(char.IsLetterOrDigit))
            .ToArray();

        return tokens.Length switch
        {
            0 => (Capitalize(email), FallbackLastName),
            1 => (Capitalize(tokens[0]), FallbackLastName),
            _ => (Capitalize(tokens[0]), Capitalize(string.Join(' ', tokens[1..])))
        };
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FallbackLastName;
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Trim().ToLowerInvariant());
    }

    private static string Coalesce(string? supplied, string derived) =>
        string.IsNullOrWhiteSpace(supplied) ? derived : supplied.Trim();
}
