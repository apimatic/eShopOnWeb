namespace Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;

/// <summary>Product family resource as returned by the Maxio API.</summary>
public class MaxioProductFamilyDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}
