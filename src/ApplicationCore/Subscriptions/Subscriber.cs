using System;
using System.Globalization;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb shopper being enrolled, resolved from the authenticated caller.
/// <para>
/// <see cref="UserName"/> is the stable link between an eShopOnWeb identity and its billing-system
/// customer: it is projected into the customer <c>reference</c>, which lets the billing system act as
/// the system of record and keeps enrollment idempotent without any local bookkeeping.
/// </para>
/// </summary>
public class Subscriber
{
    /// <summary>Prefix applied to every reference eShopOnWeb writes into the billing site, so the
    /// records it owns stay distinguishable from records created by other systems or by hand.</summary>
    public const string ReferencePrefix = "eshoponweb";

    public Subscriber(string userName, string? email = null, string? firstName = null, string? lastName = null)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        UserName = userName.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? UserName : email.Trim();

        var (derivedFirstName, derivedLastName) = DeriveName(Email);
        FirstName = string.IsNullOrWhiteSpace(firstName) ? derivedFirstName : firstName.Trim();
        LastName = string.IsNullOrWhiteSpace(lastName) ? derivedLastName : lastName.Trim();
    }

    public string UserName { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Deterministic, stable reference for this shopper's customer record in the billing system.
    /// The same eShopOnWeb user always maps to the same reference, across processes and restarts.
    /// </summary>
    public string CustomerReference => $"{ReferencePrefix}--{Sanitize(UserName)}";

    /// <summary>
    /// Deterministic reference for the shopper's subscription to a given plan. <paramref name="attempt"/>
    /// disambiguates a re-subscribe after an earlier subscription to the same plan reached end of life.
    /// </summary>
    public string SubscriptionReference(string planHandle, int attempt = 1)
    {
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Guard.Against.NegativeOrZero(attempt, nameof(attempt));

        var reference = $"{CustomerReference}--{Sanitize(planHandle)}";
        return attempt == 1 ? reference : $"{reference}--{attempt.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Reduces a value to a compact, reference-safe token so the resulting references stay readable
    /// in the billing UI and free of characters that would need escaping.
    /// </summary>
    private static string Sanitize(string value)
    {
        var sanitized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray())
            .Trim('-');

        while (sanitized.Contains("--", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);
        }

        return sanitized;
    }

    /// <summary>
    /// eShopOnWeb identities carry no given/family name, but the billing system requires both on a
    /// customer record, so derive something presentable from the email local part.
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var localPart = email.Split('@')[0];
        var tokens = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Capitalize)
            .ToArray();

        return tokens.Length switch
        {
            0 => ("eShopOnWeb", "Customer"),
            1 => (tokens[0], "Customer"),
            _ => (tokens[0], string.Join(' ', tokens.Skip(1)))
        };
    }

    private static string Capitalize(string value) =>
        value.Length <= 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value[1..];
}
