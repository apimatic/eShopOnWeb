using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb shopper a billing customer is created for. Built from the caller's authentication
/// ticket - never from the request body - so a caller can only ever act on their own subscriptions.
/// </summary>
public class SubscriberIdentity
{
    public SubscriberIdentity(string userName, string email, string? firstName = null, string? lastName = null)
    {
        UserName = Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = string.IsNullOrWhiteSpace(firstName) ? null : firstName.Trim();
        LastName = string.IsNullOrWhiteSpace(lastName) ? null : lastName.Trim();
    }

    /// <summary>The eShopOnWeb user name taken from the bearer token. Stable across restarts.</summary>
    public string UserName { get; }

    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }

    /// <summary>
    /// Best-effort given name for the billing provider, which requires a first and last name on a
    /// customer record. Falls back to the local part of the e-mail address.
    /// </summary>
    public string ResolvedFirstName => FirstName ?? Capitalize(LocalPartTokens()[0]);

    /// <summary>
    /// Best-effort family name. Uses the second token of the e-mail local part when present
    /// (jane.doe@... =&gt; "Doe"), otherwise the first label of the mail domain (...@microsoft.com =&gt; "Microsoft").
    /// </summary>
    public string ResolvedLastName
    {
        get
        {
            if (LastName is not null) return LastName;

            var tokens = LocalPartTokens();
            if (tokens.Length > 1) return Capitalize(tokens[^1]);

            var domain = Email.Contains('@', StringComparison.Ordinal)
                ? Email[(Email.IndexOf('@', StringComparison.Ordinal) + 1)..]
                : string.Empty;
            var label = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);

            return label.Length > 0 ? Capitalize(label[0]) : Capitalize(tokens[0]);
        }
    }

    private string[] LocalPartTokens()
    {
        var localPart = Email.Contains('@', StringComparison.Ordinal)
            ? Email[..Email.IndexOf('@', StringComparison.Ordinal)]
            : Email;

        var tokens = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        return tokens.Length > 0 ? tokens : new[] { localPart };
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
