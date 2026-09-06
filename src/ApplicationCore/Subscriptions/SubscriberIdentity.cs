using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb shopper, expressed in the terms the billing system needs.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Reference"/> is the stable, deterministic key that ties an eShopOnWeb user to exactly one
/// customer record in the billing system. It is derived from the user name (which is the login identity and
/// is unique in ASP.NET Identity) rather than from the Identity primary key, because the primary key is a
/// generated GUID that is re-created whenever the app runs against the in-memory database. Deriving the key
/// from the user name keeps the mapping idempotent across restarts without storing any local mapping table:
/// the billing system stays the single system of record.
/// </para>
/// </remarks>
public sealed record SubscriberIdentity
{
    /// <summary>Namespace prefix so the reference cannot collide with keys owned by another application.</summary>
    public const string ReferencePrefix = "eshoponweb-";

    private SubscriberIdentity(string userName, string reference, string email, string firstName, string lastName)
    {
        UserName = userName;
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>The eShopOnWeb login name, taken from the caller's JWT.</summary>
    public string UserName { get; }

    /// <summary>Stable idempotency key for the billing customer record.</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Builds the billing identity for an eShopOnWeb user. <paramref name="firstName"/> and
    /// <paramref name="lastName"/> are optional caller-supplied overrides; when absent they are derived from
    /// the user name so that a customer can always be created without extra input.
    /// </summary>
    public static SubscriberIdentity ForUser(string userName, string? firstName = null, string? lastName = null)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        var normalized = userName.Trim();
        var email = normalized.Contains('@', StringComparison.Ordinal)
            ? normalized
            : $"{normalized}@eshoponweb.local";

        var localPart = email[..email.IndexOf('@', StringComparison.Ordinal)];
        var separator = localPart.IndexOf('.', StringComparison.Ordinal);

        var derivedFirst = separator > 0 ? localPart[..separator] : localPart;
        var derivedLast = separator > 0 && separator < localPart.Length - 1
            ? localPart[(separator + 1)..]
            : "Customer";

        return new SubscriberIdentity(
            userName: normalized,
            reference: ReferencePrefix + normalized.ToLowerInvariant(),
            email: email,
            firstName: Normalize(firstName) ?? Titlecase(derivedFirst),
            lastName: Normalize(lastName) ?? Titlecase(derivedLast));
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Titlecase(string value) =>
        value.Length == 0 ? "Customer" : char.ToUpperInvariant(value[0]) + value[1..];
}
