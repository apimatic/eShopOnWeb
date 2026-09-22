namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The identity of an eShopOnWeb user as the billing layer needs it. <see cref="Reference"/> is
/// a stable, deterministic key derived from the authenticated identity; it is the idempotency
/// anchor used to find-or-create the matching customer in the billing provider.
/// </summary>
public record BillingCustomer
{
    public required string Reference { get; init; }

    public required string Email { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }
}
