namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class CustomerDto
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Reference { get; set; }
}
