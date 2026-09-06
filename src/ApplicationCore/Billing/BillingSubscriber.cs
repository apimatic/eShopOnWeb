using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The eShopOnWeb identity being enrolled, as resolved from the caller's bearer token.
/// </summary>
public class BillingSubscriber
{
    public BillingSubscriber(string userName, string email, string? firstName = null, string? lastName = null)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Guard.Against.NullOrWhiteSpace(email, nameof(email));

        UserName = userName;
        Email = email;
        FirstName = string.IsNullOrWhiteSpace(firstName) ? null : firstName!.Trim();
        LastName = string.IsNullOrWhiteSpace(lastName) ? null : lastName!.Trim();
    }

    /// <summary>
    /// The application's own stable identifier for the user. It is the sole input to the billing
    /// reference, so it must not change between runs — in eShopOnWeb it is the login name.
    /// </summary>
    public string UserName { get; }

    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }
}
