namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user on whose behalf a subscription is managed. The <see cref="Reference"/>
/// is the stable key used to correlate the eShop user with a billing customer (idempotently),
/// so a given user always maps to a single billing customer.
/// </summary>
public record SubscriberIdentity
{
    /// <summary>Stable unique key for the eShop user (their login name / email). Used as the billing customer reference.</summary>
    public required string Reference { get; init; }

    public required string Email { get; init; }

    /// <summary>Optional given name; a non-blank value is derived from the email when omitted.</summary>
    public string? FirstName { get; init; }

    /// <summary>Optional family name; a non-blank value is derived when omitted.</summary>
    public string? LastName { get; init; }
}
