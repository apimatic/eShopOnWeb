using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds the billing <see cref="Subscriber"/> from the caller's bearer token.
/// <para>
/// The token is the only identity source: nothing is read from the local database, so the mapping
/// between an eShopOnWeb user and their billing customer survives a restart even when the store is
/// the in-memory provider.
/// </para>
/// </summary>
public static class SubscriberFactory
{
    public const string OrganizationName = "eShopOnWeb";

    /// <summary>
    /// Produces a subscriber for the authenticated principal, or explains why the token cannot
    /// identify one.
    /// </summary>
    public static bool TryCreate(ClaimsPrincipal? principal, out Subscriber subscriber, out string error)
    {
        subscriber = null!;
        error = string.Empty;

        var userName = principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            error = "The access token does not carry a user name.";
            return false;
        }

        // eShopOnWeb signs in with the email address as the user name; fall back to an explicit email
        // claim for tokens minted by an identity provider that separates the two.
        var email = LooksLikeEmail(userName)
            ? userName
            : principal!.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email");

        if (string.IsNullOrWhiteSpace(email) || !LooksLikeEmail(email))
        {
            error = "The access token does not carry an email address, which the billing system requires to create a customer.";
            return false;
        }

        var (firstName, lastName) = DeriveName(email);

        subscriber = new Subscriber(
            ExternalId: userName.Trim(),
            Email: email.Trim(),
            FirstName: firstName,
            LastName: lastName,
            Organization: OrganizationName);

        return true;
    }

    /// <summary>
    /// Derives the given/family name pair the billing system requires from the email address, since
    /// eShopOnWeb's identity model does not store real names. "jane.doe@contoso.com" becomes
    /// "Jane Doe"; "demouser@contoso.com" becomes "Demouser Contoso".
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var atIndex = email.IndexOf('@');
        var localPart = atIndex > 0 ? email[..atIndex] : email;
        var domain = atIndex > 0 && atIndex < email.Length - 1 ? email[(atIndex + 1)..] : string.Empty;

        var tokens = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 0)
            .ToArray();

        if (tokens.Length >= 2)
        {
            return (TitleCase(tokens[0]), TitleCase(string.Join(' ', tokens.Skip(1))));
        }

        var firstName = TitleCase(tokens.Length == 1 ? tokens[0] : localPart);
        var domainLabel = domain.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        return (
            NonEmpty(firstName, "eShopOnWeb"),
            NonEmpty(TitleCase(domainLabel ?? string.Empty), "Customer"));
    }

    private static string NonEmpty(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string TitleCase(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());

    private static bool LooksLikeEmail(string value)
    {
        var atIndex = value.IndexOf('@');
        return atIndex > 0 &&
               atIndex < value.Length - 1 &&
               value.IndexOf('@', atIndex + 1) < 0 &&
               !value.Any(char.IsWhiteSpace);
    }
}
