namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing provider's record of an eShopOnWeb user.
/// </summary>
/// <remarks>
/// <see cref="Reference"/> carries the eShopOnWeb user identity (the signed-in user's
/// email/username). It is the idempotency key that lets repeated subscribe attempts find the
/// same provider-side customer instead of creating duplicates.
/// </remarks>
public sealed record BillingCustomer
{
    public required int Id { get; init; }

    public required string Reference { get; init; }

    public string? Email { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }
}
