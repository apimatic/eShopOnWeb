using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>Mirrors the <c>Customer</c> schema of the Maxio OpenAPI specification.</summary>
public class MaxioCustomer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Mirrors the <c>Customer-Response</c> wrapper: <c>{ "customer": { ... } }</c>.</summary>
public class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Mirrors the <c>Create-Customer</c> schema (the body of <c>Create-Customer-Request</c>).</summary>
public class MaxioCreateCustomer
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? Organization { get; set; }
}

/// <summary>Mirrors <c>Create-Customer-Request</c>: <c>{ "customer": { ... } }</c>.</summary>
public class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomer Customer { get; set; } = new();
}
