namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// A customer record in Maxio Advanced Billing.
/// </summary>
public class MaxioCustomer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }
    public string? Reference { get; set; }
}
