using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb identity being enrolled, together with the deterministic reference used to key the
/// matching customer record in the billing system.
/// </summary>
/// <remarks>
/// The reference is what makes enrollment idempotent: it is derived purely from the caller's stable
/// user name, so the same shopper always resolves to the same billing customer - even across
/// application restarts, and even when the local store is the in-memory provider that regenerates
/// identity primary keys on every run.
/// </remarks>
public class SubscriberIdentity
{
    private const string ReferencePrefix = "eshoponweb";

    public SubscriberIdentity(string userName, string? email = null, string? firstName = null, string? lastName = null)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A subscriber must have a user name.", nameof(userName));
        }

        UserName = userName;
        Email = string.IsNullOrWhiteSpace(email) ? userName : email!;
        Reference = BuildReference(userName);

        var derived = DeriveName(Email);
        FirstName = string.IsNullOrWhiteSpace(firstName) ? derived.First : firstName!.Trim();
        LastName = string.IsNullOrWhiteSpace(lastName) ? derived.Last : lastName!.Trim();
    }

    public string UserName { get; }
    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }

    /// <summary>Stable, unique key for this shopper in the billing system.</summary>
    public string Reference { get; }

    /// <summary>
    /// Deterministic reference for one shopper's subscription to one plan. Enrollment writes it so a
    /// subscription created by a request whose response was lost can still be found afterwards.
    /// </summary>
    public string SubscriptionReference(string planHandle) => $"{Reference}--{Sanitize(planHandle)}";

    /// <summary>
    /// Lower-cased, ASCII-safe slug plus a short digest of the original user name. The digest keeps the
    /// reference unique when two different user names sanitize to the same slug.
    /// </summary>
    private static string BuildReference(string userName)
    {
        var normalized = userName.Trim().ToLowerInvariant();
        return $"{ReferencePrefix}-{Sanitize(normalized)}-{ShortDigest(normalized)}";
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Trim().ToLowerInvariant())
        {
            var keep = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            builder.Append(keep ? c : '-');
        }

        var slug = builder.ToString().Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Length == 0 ? "user" : slug;
    }

    private static string ShortDigest(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return string.Concat(hash.Take(4).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// eShopOnWeb's identity store holds no personal names, but the billing provider requires a first
    /// and last name on a customer. Derive a deterministic placeholder from the email local part;
    /// callers who know the real name can override it on the enrollment request.
    /// </summary>
    private static (string First, string Last) DeriveName(string email)
    {
        var localPart = email.Split('@')[0];
        var tokens = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(TitleCase)
            .ToArray();

        if (tokens.Length == 0)
        {
            return ("eShopOnWeb", "Customer");
        }

        return tokens.Length == 1
            ? (tokens[0], "Customer")
            : (tokens[0], string.Join(" ", tokens.Skip(1)));
    }

    private static string TitleCase(string token) =>
        token.Length <= 1 ? token.ToUpperInvariant() : char.ToUpperInvariant(token[0]) + token.Substring(1);
}
