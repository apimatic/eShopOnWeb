using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>Wire model for the specification's <c>Customer Response</c> schema.</summary>
public class MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Wire model for the specification's <c>Customer</c> schema (only the fields this integration uses).</summary>
public class MaxioCustomer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Request body for <c>createCustomer</c>: the specification's <c>Create Customer Request</c> schema.</summary>
public class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomer Customer { get; set; } = new();
}

/// <summary>Wire model for the specification's <c>Create Customer</c> schema.</summary>
public class MaxioCreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}
