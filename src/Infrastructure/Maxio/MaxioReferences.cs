using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Builds the references eShopOnWeb assigns to Maxio customers and subscriptions.
/// </summary>
/// <remarks>
/// <para>
/// References are the hinge the whole integration turns on. Maxio enforces uniqueness on the
/// customer reference and on the subscription reference, so a reference that is derived
/// deterministically from stable inputs turns "create if missing" into an operation that is safe to
/// repeat: the second attempt is refused by the provider instead of quietly creating a duplicate.
/// </para>
/// <para>
/// A reference is a readable slug followed by a short digest of the exact input. The slug makes a
/// record recognisable in the Maxio UI; the digest guarantees that two different inputs which slug
/// to the same text (say <c>a.b@example.com</c> and <c>a-b@example.com</c>) still get different
/// references.
/// </para>
/// </remarks>
public static class MaxioReferences
{
    private const int MaxSlugLength = 48;
    private const int DigestLength = 8;

    /// <summary>
    /// Builds the customer reference for a shopper, from their e-mail address.
    /// </summary>
    public static string ForCustomer(string referencePrefix, string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var normalized = email.Trim().ToLowerInvariant();
        return Join(Slug(referencePrefix), Slug(normalized), Digest(normalized));
    }

    /// <summary>
    /// Builds the reference for a shopper's subscription to a plan.
    /// </summary>
    /// <param name="customerReference">The reference of the customer being subscribed.</param>
    /// <param name="planHandle">Handle of the plan being subscribed to.</param>
    /// <param name="idempotencyKey">
    /// Optional caller-supplied key. Two calls carrying the same key resolve to the same reference,
    /// and therefore to the same subscription.
    /// </param>
    public static string ForSubscription(string customerReference, string planHandle, string? idempotencyKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(planHandle);

        var reference = Join(customerReference, Slug(planHandle));

        return string.IsNullOrWhiteSpace(idempotencyKey)
            ? reference
            : Join(reference, Digest(idempotencyKey.Trim()));
    }

    /// <summary>
    /// Returns the <paramref name="sequence"/>th variant of a reference, used when the shopper is
    /// subscribing again to a plan they previously held and the plain reference is already spent.
    /// </summary>
    public static string WithSequence(string reference, int sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 2);

        return Join(reference, sequence.ToString(CultureInfo.InvariantCulture));
    }

    private static string Join(params string[] parts) => string.Join('-', Array.FindAll(parts, p => p.Length > 0));

    /// <summary>
    /// Reduces arbitrary text to lowercase alphanumerics separated by single hyphens.
    /// </summary>
    internal static string Slug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var lastWasSeparator = true;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }

            if (builder.Length >= MaxSlugLength)
            {
                break;
            }
        }

        return builder.ToString().Trim('-');
    }

    /// <summary>
    /// A short, stable, lowercase hexadecimal digest of the exact input.
    /// </summary>
    internal static string Digest(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..DigestLength].ToLowerInvariant();
    }
}
