namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb shopper a billing customer is created for. <see cref="UserName"/> is the stable
/// identity that ties the shopper to their customer record at the billing provider.
/// </summary>
public record SubscriberIdentity
{
    public required string UserName { get; init; }

    public string? Email { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public string? Organization { get; init; }
}
