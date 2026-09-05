namespace Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

internal class CustomerEnvelope
{
    public CustomerWire? Customer { get; set; }
}

internal class CustomerWire
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal class CreateCustomerRequestEnvelope
{
    public CreateCustomerRequestWire Customer { get; set; } = new();
}

internal class CreateCustomerRequestWire
{
    public string Reference { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}
