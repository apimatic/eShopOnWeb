using System;
using System.Globalization;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb shopper on whose behalf a billing operation runs. Built from the authenticated
/// caller's identity - never from request input - so a caller can only ever act on their own
/// subscriptions.
/// </summary>
public sealed class SubscriberIdentity
{
    /// <summary>
    /// Prefix applied to every reference eShopOnWeb writes into Maxio, so records created by this
    /// application are recognisable on a shared site.
    /// </summary>
    public const string ReferencePrefix = "eshop";

    public SubscriberIdentity(string userName, string email, string? firstName = null, string? lastName = null)
    {
        UserName = Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = string.IsNullOrWhiteSpace(firstName) ? null : firstName.Trim();
        LastName = string.IsNullOrWhiteSpace(lastName) ? null : lastName.Trim();
    }

    /// <summary>The eShopOnWeb user name (the identity carried by the JWT's name claim).</summary>
    public string UserName { get; }

    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }

    /// <summary>
    /// The stable key that ties this shopper to exactly one Maxio customer record. The user name is
    /// used rather than the Identity primary key because the sample runs against an in-memory store
    /// whose generated keys do not survive a restart, while user names do.
    /// </summary>
    public string BillingReference => $"{ReferencePrefix}:{UserName.Trim().ToLowerInvariant()}";

    /// <summary>
    /// Given name to register with Maxio, which requires a non-blank first and last name. Falls back
    /// to a value derived from the e-mail local part when the shopper has no profile name.
    /// </summary>
    public string ResolvedFirstName => FirstName ?? DerivedNames().First;

    /// <summary>Family name to register with Maxio. See <see cref="ResolvedFirstName"/>.</summary>
    public string ResolvedLastName => LastName ?? DerivedNames().Last;

    private (string First, string Last) DerivedNames()
    {
        var localPart = Email.Split('@')[0];
        var tokens = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 0)
            .ToArray();

        if (tokens.Length == 0)
        {
            return ("eShopOnWeb", "Customer");
        }

        var first = TitleCase(tokens[0]);
        var last = tokens.Length > 1 ? TitleCase(string.Join(" ", tokens.Skip(1))) : "Customer";
        return (first, last);
    }

    private static string TitleCase(string value) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
}
