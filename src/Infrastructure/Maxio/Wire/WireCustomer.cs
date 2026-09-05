namespace Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

// Mirrors maxio-spec/components/schemas/Customer.yaml (only the fields eShopOnWeb consumes).
internal class WireCustomer
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal class CustomerEnvelope
{
    public WireCustomer? Customer { get; set; }
}

// Mirrors maxio-spec/components/schemas/Create-Customer.yaml.
internal class CreateWireCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal class CreateCustomerEnvelope
{
    public CreateWireCustomer Customer { get; set; } = new();
}
