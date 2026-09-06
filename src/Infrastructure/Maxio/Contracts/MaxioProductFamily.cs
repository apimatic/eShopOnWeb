namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>Maxio product family, as returned nested inside a product.</summary>
public class MaxioProductFamily
{
    public long Id { get; set; }

    public string? Name { get; set; }

    public string? Handle { get; set; }

    public string? Description { get; set; }
}
