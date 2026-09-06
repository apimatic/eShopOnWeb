using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The eShopOnWeb shopper a billing record belongs to, as resolved from the caller's identity.
/// </summary>
/// <param name="Key">
/// The application's durable identifier for this shopper. It is embedded in the billing provider's
/// customer reference, so it must be stable for the lifetime of the account.
/// </param>
/// <param name="Email">The shopper's email address; required by the billing provider.</param>
/// <param name="FirstName">Given name to store on the billing customer record.</param>
/// <param name="LastName">Family name to store on the billing customer record.</param>
/// <param name="Organization">Optional company name to store on the billing customer record.</param>
public sealed record BillingSubscriber(
    string Key,
    string Email,
    string FirstName,
    string LastName,
    string? Organization = null)
{
    /// <summary>Throws when a field the billing provider requires is missing.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Key))
            throw new ArgumentException("A billing subscriber key is required.", nameof(Key));
        if (string.IsNullOrWhiteSpace(Email))
            throw new ArgumentException("A billing subscriber email is required.", nameof(Email));
        if (string.IsNullOrWhiteSpace(FirstName))
            throw new ArgumentException("A billing subscriber first name is required.", nameof(FirstName));
        if (string.IsNullOrWhiteSpace(LastName))
            throw new ArgumentException("A billing subscriber last name is required.", nameof(LastName));
    }
}
