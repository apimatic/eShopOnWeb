namespace Maxio.Models;

/// <summary>
/// A Maxio product family. Mirrors the Product Family schema in the Maxio OpenAPI specification.
/// </summary>
public class ProductFamily
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public string? AccountingCode { get; set; }
}
