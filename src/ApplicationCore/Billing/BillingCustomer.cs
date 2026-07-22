namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A billing-provider customer record linked to an eShopOnWeb user through <see cref="Reference"/>.
/// </summary>
public class BillingCustomer
{
    public int Id { get; set; }

    /// <summary>
    /// The stable eShopOnWeb user reference (the signed-in user's email/username). This is what makes
    /// customer creation idempotent across repeated subscribe attempts.
    /// </summary>
    public string? Reference { get; set; }

    public string? Email { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }
}
