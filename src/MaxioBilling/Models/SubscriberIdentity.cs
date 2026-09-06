using System.Globalization;

namespace Microsoft.eShopWeb.MaxioBilling.Models;

/// <summary>
/// The eShopOnWeb user a billing operation acts for, together with the stable key that
/// identifies their Maxio customer record.
/// </summary>
public sealed record SubscriberIdentity
{
    /// <summary>
    /// Prefix on every reference this application owns, so eShopOnWeb customers stay
    /// distinguishable from anything else on a shared Maxio site.
    /// </summary>
    public const string ReferencePrefix = "eshoponweb-";

    private SubscriberIdentity(string userName, string reference, string email, string firstName, string lastName)
    {
        UserName = userName;
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>The eShopOnWeb login name taken from the caller's token.</summary>
    public string UserName { get; }

    /// <summary>
    /// The Maxio customer reference. Maxio enforces this as unique per site and it is the only
    /// exact-match customer lookup key, so it is what makes "ensure a customer exists" idempotent.
    /// It is derived from the login name rather than the ASP.NET Identity id because the identity
    /// store is re-seeded on every run in this sample, while the login name is stable.
    /// </summary>
    public string Reference { get; }

    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }

    /// <summary>Builds the identity for a login name, deriving a stable reference and billing name.</summary>
    /// <exception cref="ArgumentException">The login name is missing.</exception>
    public static SubscriberIdentity ForUser(string userName, string? email = null)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user name is required to identify the billing customer.", nameof(userName));
        }

        var normalized = userName.Trim().ToLowerInvariant();
        var resolvedEmail = string.IsNullOrWhiteSpace(email) ? normalized : email.Trim();
        var (firstName, lastName) = DeriveName(resolvedEmail);

        return new SubscriberIdentity(
            userName: userName.Trim(),
            reference: ReferencePrefix + normalized,
            email: resolvedEmail,
            firstName: firstName,
            lastName: lastName);
    }

    /// <summary>
    /// Maxio requires a first and last name on every customer, but eShopOnWeb only stores a login.
    /// Derive both deterministically from the address so the same user always maps to the same record.
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var localPart = email.Split('@')[0];
        var tokens = localPart
            .Split(['.', '_', '-', '+'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Capitalize)
            .Where(token => token.Length > 0)
            .ToArray();

        return tokens.Length switch
        {
            0 => ("eShopOnWeb", "Customer"),
            1 => (tokens[0], "Customer"),
            _ => (tokens[0], string.Join(' ', tokens.Skip(1)))
        };
    }

    private static string Capitalize(string value) =>
        value.Length == 0
            ? value
            : char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];
}
