using System;
using System.Globalization;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb account on whose behalf we talk to the billing system. Built from the
/// authenticated caller's identity - never from anything the caller sends in a request body.
/// </summary>
public class SubscriberIdentity
{
    public SubscriberIdentity(string userId, string email, string firstName, string lastName)
    {
        UserId = Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email)).Trim();
        FirstName = Guard.Against.NullOrWhiteSpace(firstName, nameof(firstName));
        LastName = Guard.Against.NullOrWhiteSpace(lastName, nameof(lastName));
    }

    /// <summary>The ASP.NET Identity user id.</summary>
    public string UserId { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Builds an identity for an eShopOnWeb account. The store only keeps a username and an email,
    /// so the billing system's mandatory first/last name are derived from the email's local part.
    /// </summary>
    public static SubscriberIdentity FromAccount(string userId, string email)
    {
        Guard.Against.NullOrWhiteSpace(email, nameof(email));

        var localPart = email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var firstName = parts.Length > 0 ? Titleize(parts[0]) : "eShopOnWeb";
        var lastName = parts.Length > 1 ? Titleize(parts[^1]) : "Subscriber";

        return new SubscriberIdentity(userId, email, firstName, lastName);
    }

    private static string Titleize(string value) =>
        value.Length <= 1
            ? value.ToUpper(CultureInfo.InvariantCulture)
            : char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];
}
