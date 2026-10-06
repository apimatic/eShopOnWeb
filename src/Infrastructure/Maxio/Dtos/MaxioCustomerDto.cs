namespace Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;

/// <summary>Customer resource as returned by the Maxio API.</summary>
public class MaxioCustomerDto
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? Organization { get; set; }
}
