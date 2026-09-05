namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Maxio;

public class MaxioCustomerDto
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string Email { get; set; } = string.Empty;
}
