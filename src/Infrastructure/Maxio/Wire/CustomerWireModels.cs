namespace Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

internal sealed class CustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string Email { get; set; } = string.Empty;
}

internal sealed class CreateCustomerEnvelope
{
    public CreateCustomerAttributes Customer { get; set; } = new();
}

internal sealed class CreateCustomerAttributes
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}
