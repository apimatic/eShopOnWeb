using System;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb shopper, expressed in the terms the billing system needs to identify them.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Reference"/> is the durable link between an eShopOnWeb identity and its billing-system
/// customer record. It is derived from the user name (which is the shopper's e-mail address in
/// eShopOnWeb) rather than from the ASP.NET Identity primary key on purpose: the reference has to
/// survive an identity store that is re-created on every process start (see the in-memory database
/// configuration), otherwise every restart would orphan the shopper's billing history.
/// </para>
/// </remarks>
public sealed class Subscriber : IEquatable<Subscriber>
{
    /// <summary>Prefix applied to every reference so that records owned by this application are obvious in the billing system.</summary>
    public const string ReferencePrefix = "eshoponweb-";

    public Subscriber(string reference, string email, string firstName, string lastName)
    {
        Reference = Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = Guard.Against.NullOrWhiteSpace(firstName, nameof(firstName));
        LastName = Guard.Against.NullOrWhiteSpace(lastName, nameof(lastName));
    }

    /// <summary>Stable, unique identifier for this shopper inside the billing system.</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Projects an authenticated eShopOnWeb identity onto a <see cref="Subscriber"/>.
    /// </summary>
    /// <param name="userName">The authenticated user name taken from the caller's token.</param>
    /// <param name="email">The user's e-mail address; falls back to <paramref name="userName"/> when not set.</param>
    public static Subscriber FromIdentity(string userName, string? email = null)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        var normalizedUserName = userName.Trim().ToLowerInvariant();
        var effectiveEmail = string.IsNullOrWhiteSpace(email) ? normalizedUserName : email.Trim();
        var (firstName, lastName) = DeriveName(effectiveEmail);

        return new Subscriber($"{ReferencePrefix}{normalizedUserName}", effectiveEmail, firstName, lastName);
    }

    /// <summary>
    /// eShopOnWeb identities carry no given/family name, but the billing system requires both.
    /// Derive something human-readable from the local part of the e-mail address.
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var localPart = email.Split('@')[0];
        var words = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var first = words.Length > 0 ? Titleize(words[0]) : "eShopOnWeb";
        var last = words.Length > 1 ? string.Join(' ', words[1..].Select(Titleize)) : "Customer";

        return (first, last);
    }

    private static string Titleize(string value) =>
        value.Length <= 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value[1..];

    public bool Equals(Subscriber? other) =>
        other is not null && string.Equals(Reference, other.Reference, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as Subscriber);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Reference);

    public override string ToString() => Reference;
}
