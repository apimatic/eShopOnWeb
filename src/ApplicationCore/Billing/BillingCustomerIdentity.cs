using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The eShopOnWeb shopper as the billing system sees them.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CustomerReference"/> and <see cref="SubscriptionReference"/> are the idempotency keys for the
/// whole capability: they are derived deterministically from the shopper's e-mail address, so the same
/// shopper always maps to the same billing customer and the same shopper/plan pair always maps to the same
/// subscription, no matter how many times the subscribe endpoint is called.
/// </para>
/// <para>
/// The e-mail address (rather than the ASP.NET Identity primary key) is the basis on purpose: eShopOnWeb has
/// no change-e-mail flow, while the identity primary key is a fresh GUID on every run when the app is hosted
/// on the in-memory database. Deriving from the e-mail keeps the mapping stable across restarts. The slug is
/// suffixed with a short hash of the address so that two addresses which slugify identically
/// (<c>a.b@x.com</c> and <c>a-b@x.com</c>) still get distinct references.
/// </para>
/// </remarks>
public sealed class BillingCustomerIdentity
{
    private const string ReferencePrefix = "eshoponweb";

    private BillingCustomerIdentity(string email, string firstName, string lastName, string customerReference)
    {
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        CustomerReference = customerReference;
    }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>Stable, unique-per-site key identifying this shopper's billing customer record.</summary>
    public string CustomerReference { get; }

    /// <summary>Stable key identifying this shopper's subscription to a single plan.</summary>
    public string SubscriptionReference(string planHandle) =>
        $"{CustomerReference}-{Slugify(planHandle)}";

    public static BillingCustomerIdentity FromEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("An e-mail address is required to identify a billing customer.", nameof(email));
        }

        var normalized = email.Trim().ToLowerInvariant();
        var (firstName, lastName) = DeriveName(normalized);

        return new BillingCustomerIdentity(
            normalized,
            firstName,
            lastName,
            $"{ReferencePrefix}-{Slugify(normalized)}-{ShortHash(normalized)}");
    }

    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var localPart = email.Split('@')[0];
        var parts = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Capitalize)
            .ToArray();

        return parts.Length switch
        {
            0 => ("eShopOnWeb", "Customer"),
            1 => (parts[0], "Customer"),
            _ => (parts[0], string.Join(" ", parts.Skip(1)))
        };
    }

    private static string Capitalize(string value) =>
        value.Length <= 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value.Substring(1);

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasSeparator = false;

        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var builder = new StringBuilder(8);

        for (var i = 0; i < 4; i++)
        {
            builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
