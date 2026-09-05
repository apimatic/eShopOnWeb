namespace Microsoft.eShopWeb.Infrastructure.Maxio.Json;

internal sealed class CustomerEnvelope
{
    public CustomerJson? Customer { get; set; }
}

internal sealed class CustomerJson
{
    public int? Id { get; set; }
    public string? Reference { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
}
