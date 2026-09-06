using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The authenticated shopper on whose behalf the billing system is called.
/// Built from the caller's bearer token by the API layer; never accepted from a request body.
/// </summary>
public record SubscriberIdentity
{
    /// <summary>
    /// Stable application-side identifier for the shopper. It is folded into the billing
    /// system's customer <c>reference</c>, so it has to be the same value on every visit.
    /// </summary>
    public required string UserId { get; init; }

    public required string Email { get; init; }

    public string? FirstName { get; init; }
    public string? LastName { get; init; }

    /// <summary>
    /// A first/last name pair suitable for the billing system, which requires both.
    /// eShopOnWeb identities carry no name, so the email local part is used as a fallback.
    /// </summary>
    public (string First, string Last) ResolveName()
    {
        var first = string.IsNullOrWhiteSpace(FirstName) ? null : FirstName.Trim();
        var last = string.IsNullOrWhiteSpace(LastName) ? null : LastName.Trim();

        if (first is not null && last is not null)
        {
            return (first, last);
        }

        var localPart = Email.Split('@')[0];
        var separatorIndex = localPart.IndexOfAny(new[] { '.', '_', '-', '+' });

        if (first is null && last is null && separatorIndex > 0 && separatorIndex < localPart.Length - 1)
        {
            return (Capitalize(localPart[..separatorIndex]), Capitalize(localPart[(separatorIndex + 1)..]));
        }

        return (first ?? Capitalize(localPart), last ?? "eShopOnWeb");
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
