namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

internal sealed class CustomerEnvelope
{
    public CustomerResource? Customer { get; set; }
}

internal sealed class CustomerResource
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }
}

/// <summary>Request body for POST /customers.json.</summary>
internal sealed class CreateCustomerRequest
{
    public CreateCustomerAttributes Customer { get; set; } = new();
}

internal sealed class CreateCustomerAttributes
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? Organization { get; set; }
}
