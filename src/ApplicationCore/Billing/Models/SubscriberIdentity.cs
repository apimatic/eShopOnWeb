namespace Microsoft.eShopWeb.ApplicationCore.Billing.Models;

/// <summary>
/// The eShopOnWeb-side identity of the shopper being billed. Everything the billing provider
/// needs in order to create or locate the matching customer record is carried here, so the
/// billing layer never has to reach back into ASP.NET Identity.
/// </summary>
public sealed record SubscriberIdentity
{
    /// <summary>The ASP.NET Identity user id.</summary>
    public required string UserId { get; init; }

    /// <summary>The email address of the shopper; also their eShopOnWeb user name.</summary>
    public required string Email { get; init; }

    /// <summary>Optional given name supplied by the caller; derived from the email when absent.</summary>
    public string? FirstName { get; init; }

    /// <summary>Optional family name supplied by the caller; derived from the email when absent.</summary>
    public string? LastName { get; init; }
}
