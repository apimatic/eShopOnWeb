namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public class MaxioNewCustomerRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string? Organization { get; set; }
}
