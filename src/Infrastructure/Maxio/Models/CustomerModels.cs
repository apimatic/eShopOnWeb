namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// Wire models mirroring the Maxio OpenAPI spec (components/schemas/Customer.yaml,
// Create-Customer.yaml and their { "customer": ... } envelopes). Property names are
// serialized to snake_case by the shared JsonSerializerOptions.

/// <summary>Envelope for a single customer, per Customer-Response.yaml.</summary>
public class CustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Subset of Customer.yaml used by this integration.</summary>
public class MaxioCustomer
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Organization { get; set; }
}

/// <summary>Envelope for Create-Customer-Request.yaml.</summary>
public class CreateCustomerEnvelope
{
    public CreateCustomer Customer { get; set; } = new();
}

/// <summary>Fields we send when creating a customer, per Create-Customer.yaml (first/last/email required).</summary>
public class CreateCustomer
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}
