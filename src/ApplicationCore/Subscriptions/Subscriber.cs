namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb identity being billed. <see cref="UserKey"/> is the value the billing
/// system stores as its customer reference, so it must be stable for the lifetime of the
/// account: it is the only thing tying an eShopOnWeb user to their billing customer record.
/// </summary>
public class Subscriber
{
    public required string UserKey { get; init; }

    public required string Email { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public string? Organization { get; init; }
}
