using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb identity that a billing-provider customer is created for.
/// </summary>
/// <remarks>
/// <see cref="UserIdentifier"/> must be stable for the lifetime of the account, because it is
/// what links this application's user to the provider's customer record. The authenticated
/// user name is used rather than the ASP.NET Identity row id: the row id is regenerated whenever
/// the app runs against the in-memory store, which would orphan the provider customer on restart.
/// </remarks>
public class BillingCustomerProfile
{
    public BillingCustomerProfile(string userIdentifier, string email, string? firstName = null, string? lastName = null)
    {
        UserIdentifier = Guard.Against.NullOrWhiteSpace(userIdentifier, nameof(userIdentifier));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = firstName;
        LastName = lastName;
    }

    public string UserIdentifier { get; }

    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }
}
