namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// The billing-provider customer record that an eShopOnWeb user maps onto.
/// </summary>
public class BillingCustomer
{
    public int Id { get; set; }

    /// <summary>
    /// The stable eShopOnWeb user reference (email / username) this customer was created with.
    /// </summary>
    public string? Reference { get; set; }

    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}
