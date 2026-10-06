namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// Data used to register a customer in the billing system.
/// </summary>
public class NewBillingCustomer
{
    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Unique application-side identifier for the customer (the shopper's account email).
    /// The billing system enforces one customer per reference, which is what makes
    /// customer provisioning idempotent.
    /// </summary>
    public string Reference { get; init; } = string.Empty;
}
