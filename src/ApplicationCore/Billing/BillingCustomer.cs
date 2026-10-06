namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A customer record in the billing system of record (Maxio Advanced Billing).
/// </summary>
public class BillingCustomer
{
    /// <summary>The billing provider's numeric customer id.</summary>
    public long Id { get; init; }

    /// <summary>The application-side stable identifier stored back on the provider customer (unique per provider customer).</summary>
    public string Reference { get; init; } = string.Empty;

    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}

/// <summary>
/// Values used to create a new billing customer.
/// </summary>
public class BillingCustomerDraft
{
    public string Reference { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}
