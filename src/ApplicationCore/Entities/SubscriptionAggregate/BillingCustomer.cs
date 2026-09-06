namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing-provider customer record that an eShopOnWeb user maps onto.
/// <para>
/// Projected from the Maxio <c>Customer</c> schema (<c>components/schemas/Customer.yaml</c>).
/// The link between an eShopOnWeb user and this record is the provider-side
/// <see cref="Reference"/>, so no local mapping table is required.
/// </para>
/// </summary>
public class BillingCustomer
{
    public int Id { get; init; }

    /// <summary>Our own stable identifier for the user, stored on the provider (Maxio <c>customer.reference</c>).</summary>
    public string? Reference { get; init; }

    public string? Email { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public string? Organization { get; init; }
}
