using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Wire;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Turns an eShopOnWeb shopper into the customer record Maxio expects, and derives the
/// deterministic keys the subscribe flow relies on for idempotency.
/// </summary>
internal static class MaxioCustomerMapping
{
    /// <summary>
    /// The <c>reference</c> written onto the Maxio customer. Maxio enforces that at most one
    /// customer exists per reference, which is what makes "ensure a customer exists" safe to call
    /// concurrently.
    /// </summary>
    public static string CustomerReference(string prefix, SubscriberIdentity subscriber)
    {
        var userKey = subscriber.UserName.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(prefix) ? userKey : $"{prefix.Trim()}:{userKey}";
    }

    /// <summary>
    /// A stable, human-readable reference for the subscription itself.
    /// <paramref name="generation"/> is the number of subscriptions the shopper has already had
    /// on this plan, so re-subscribing after a cancellation produces a fresh reference instead of
    /// colliding with the old one.
    /// </summary>
    public static string SubscriptionReference(string customerReference, string planHandle, int generation) =>
        $"{customerReference}:{planHandle}:{generation.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// The <c>uniqueness_token</c> sent with a subscription create. Maxio rejects a repeat of the
    /// same token inside 60 minutes with 409, so a double-clicked or retried signup cannot enroll
    /// twice even if it reaches Maxio through a different application instance.
    /// </summary>
    /// <remarks>
    /// The generation is part of the hash so a genuine re-subscribe after cancelling is not
    /// mistaken for a replay of the original signup.
    /// </remarks>
    public static string UniquenessToken(
        string customerReference,
        string planHandle,
        int generation,
        string? callerIdempotencyKey)
    {
        var material = string.Join(
            '|',
            "eshoponweb-subscribe",
            customerReference,
            planHandle,
            generation.ToString(CultureInfo.InvariantCulture),
            callerIdempotencyKey?.Trim() ?? string.Empty);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Builds the customer payload. Maxio requires a first and last name; ASP.NET Identity in
    /// eShopOnWeb stores neither, so a name supplied by the caller wins and otherwise one is
    /// derived from the email address (jane.doe@contoso.com becomes "Jane Doe",
    /// demouser@microsoft.com becomes "Demouser Microsoft").
    /// </summary>
    public static MaxioCustomerAttributes ToCustomerAttributes(SubscriberIdentity subscriber, string reference)
    {
        var (derivedFirst, derivedLast) = DeriveName(subscriber.EmailAddress);

        var firstName = Coalesce(subscriber.FirstName, derivedFirst);
        var lastName = Coalesce(subscriber.LastName, derivedLast);

        return new MaxioCustomerAttributes
        {
            FirstName = firstName,
            LastName = lastName,
            Email = subscriber.EmailAddress,
            Reference = reference
        };
    }

    internal static (string First, string Last) DeriveName(string emailOrUserName)
    {
        var value = emailOrUserName?.Trim() ?? string.Empty;
        var at = value.IndexOf('@');
        var localPart = at > 0 ? value[..at] : value;
        var domain = at >= 0 && at < value.Length - 1 ? value[(at + 1)..] : string.Empty;

        var tokens = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Titlecase)
            .Where(t => t.Length > 0)
            .ToArray();

        var first = tokens.Length > 0 ? tokens[0] : "eShopOnWeb";

        if (tokens.Length > 1)
        {
            return (first, string.Join(' ', tokens.Skip(1)));
        }

        // Only one token to work with, so fall back to the organisation implied by the domain -
        // Maxio rejects a blank last name outright.
        var domainLabel = domain.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var last = Titlecase(domainLabel ?? string.Empty);

        return (first, string.IsNullOrWhiteSpace(last) ? "Customer" : last);
    }

    private static string Coalesce(string? preferred, string fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred!.Trim();

    private static string Titlecase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }
}
