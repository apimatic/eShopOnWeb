namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// A customer as known to the billing system of record.
/// </summary>
public class BillingCustomer
{
    /// <summary>The billing system's numeric id for the customer.</summary>
    public int Id { get; init; }

    /// <summary>The unique application-side reference stored on the customer (the shopper's account email).</summary>
    public string? Reference { get; init; }

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;
}
