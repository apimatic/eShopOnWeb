namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>A Maxio customer record.</summary>
public class MaxioCustomer
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}
