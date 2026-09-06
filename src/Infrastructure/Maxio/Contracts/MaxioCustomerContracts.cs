using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>
/// Maxio <c>Customer</c> (<c>components/schemas/Customer.yaml</c>). Only the fields this integration
/// reads are modelled; unknown members are ignored on deserialization.
/// </summary>
public record MaxioCustomer
{
    public int Id { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? CcEmails { get; init; }
    public string? Organization { get; init; }
    public string? Reference { get; init; }
    public string? Locale { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>Maxio <c>Customer Response</c> (<c>components/schemas/Customer-Response.yaml</c>).</summary>
public record MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; init; }
}

/// <summary>
/// Maxio <c>Create Customer</c> (<c>components/schemas/Create-Customer.yaml</c>).
/// <c>first_name</c>, <c>last_name</c> and <c>email</c> are required by the schema.
/// </summary>
public record MaxioCreateCustomer
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public string? Organization { get; init; }
    public string? Reference { get; init; }
}

/// <summary>Maxio <c>Create Customer Request</c> (<c>components/schemas/Create-Customer-Request.yaml</c>).</summary>
public record MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public required MaxioCreateCustomer Customer { get; init; }
}
