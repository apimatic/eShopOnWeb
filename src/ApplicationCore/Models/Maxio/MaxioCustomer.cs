namespace Microsoft.eShopWeb.ApplicationCore.Models.Maxio;

/// <summary>
/// Application-facing view of a Maxio Advanced Billing customer.
/// </summary>
public class MaxioCustomer
{
    public long Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}
