namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A customer record in Maxio Advanced Billing, keyed to an eShopOnWeb user via <see cref="Reference"/>.
/// </summary>
public class MaxioCustomer
{
    public long Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}
