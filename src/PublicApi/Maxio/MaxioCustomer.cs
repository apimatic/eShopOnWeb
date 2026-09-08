using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// A Maxio Customer resource (see components/schemas/Customer.yaml of the Maxio spec).
/// </summary>
public sealed class MaxioCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>GET /customers/lookup.json and POST /customers.json response wrapper.</summary>
public sealed class MaxioCustomerResponse
{
    public MaxioCustomer Customer { get; set; } = new();
}

/// <summary>POST /customers.json request body: <c>{ "customer": { ... } }</c>.</summary>
public sealed class CreateMaxioCustomerRequest
{
    public MaxioCustomerInput Customer { get; set; } = new();
}

/// <summary>
/// Customer create payload. first_name/last_name/email are required by Maxio and the
/// reference must be unique site-wide.
/// </summary>
public sealed class MaxioCustomerInput
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}
