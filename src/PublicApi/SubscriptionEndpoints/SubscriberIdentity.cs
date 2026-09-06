using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Derives the billing subscriber from the caller's bearer token.
/// </summary>
/// <remarks>
/// Identity comes from the token and nowhere else — no user id is accepted from the request body,
/// so a caller can only ever act on their own subscriptions.
/// </remarks>
public static class SubscriberIdentity
{
    /// <summary>
    /// Stands in for a surname the application does not hold, so the billing record reads as
    /// deliberately blank rather than as invented data. Maxio rejects an empty last name.
    /// </summary>
    public const string UnknownNamePlaceholder = "(unspecified)";

    private static readonly char[] NameSeparators = { '.', '_', '-', '+' };

    /// <summary>
    /// Builds the subscriber for <paramref name="principal"/>, or <c>null</c> when the token
    /// carries no usable identity.
    /// </summary>
    /// <param name="principal">The authenticated caller.</param>
    /// <param name="firstName">Given name supplied by the caller, if any; overrides the derived one.</param>
    /// <param name="lastName">Family name supplied by the caller, if any; overrides the derived one.</param>
    /// <param name="organization">Company name supplied by the caller, if any.</param>
    public static BillingSubscriber? Resolve(
        ClaimsPrincipal? principal,
        string? firstName = null,
        string? lastName = null,
        string? organization = null)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        // eShopOnWeb signs in by email, and the email is what identifies the shopper to a billing
        // system, so it is the durable key here. Preferring the email claim keeps this correct even
        // if usernames stop being email addresses.
        var email = FirstNonEmpty(
            principal.FindFirstValue(ClaimTypes.Email),
            principal.FindFirstValue("email"),
            principal.FindFirstValue(ClaimTypes.Name),
            principal.Identity?.Name);

        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var (derivedFirst, derivedLast) = DeriveName(
            email!,
            principal.FindFirstValue(ClaimTypes.GivenName),
            principal.FindFirstValue(ClaimTypes.Surname));

        return new BillingSubscriber(
            Key: email!.Trim().ToLowerInvariant(),
            Email: email!.Trim(),
            FirstName: Coalesce(firstName, derivedFirst),
            LastName: Coalesce(lastName, derivedLast),
            Organization: string.IsNullOrWhiteSpace(organization) ? null : organization!.Trim());
    }

    /// <summary>
    /// Works a display name out of the claims, falling back to the email's local part —
    /// "ada.lovelace@example.com" becomes "Ada Lovelace".
    /// </summary>
    public static (string FirstName, string LastName) DeriveName(string email, string? givenName, string? surname)
    {
        if (!string.IsNullOrWhiteSpace(givenName) || !string.IsNullOrWhiteSpace(surname))
        {
            return (
                string.IsNullOrWhiteSpace(givenName) ? UnknownNamePlaceholder : givenName!.Trim(),
                string.IsNullOrWhiteSpace(surname) ? UnknownNamePlaceholder : surname!.Trim());
        }

        var atIndex = email.IndexOf('@');
        var localPart = atIndex > 0 ? email[..atIndex] : email;

        var tokens = localPart
            .Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(TitleCase)
            .Where(t => t.Length > 0)
            .ToArray();

        return tokens.Length switch
        {
            0 => (email.Trim(), UnknownNamePlaceholder),
            1 => (tokens[0], UnknownNamePlaceholder),
            _ => (tokens[0], string.Join(' ', tokens.Skip(1))),
        };
    }

    private static string TitleCase(string value) =>
        value.Length == 0
            ? value
            : char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];

    private static string Coalesce(string? preferred, string fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred!.Trim();

    private static string? FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
}
