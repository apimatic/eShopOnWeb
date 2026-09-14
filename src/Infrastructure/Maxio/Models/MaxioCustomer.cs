using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Wire model for a Maxio <c>customer</c>, mirroring the fields of the <c>Customer</c>
/// schema that this integration reads and writes.
/// </summary>
public class MaxioCustomer
{
    public int Id { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    /// <summary>Your app's unique identifier for the customer (the idempotency key).</summary>
    public string? Reference { get; set; }

    public string? Organization { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Envelope for a single customer, per the <c>Customer-Response</c> schema.</summary>
public class MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Request body for creating a customer, per the <c>Create-Customer-Request</c> schema.</summary>
public class CreateCustomerRequest
{
    public CreateCustomer Customer { get; set; } = new();
}

/// <summary>
/// The customer attributes accepted on create, per the <c>Create-Customer</c> schema.
/// Only the fields this integration sets are modeled; <c>first_name</c>, <c>last_name</c>
/// and <c>email</c> are required by the contract.
/// </summary>
public class CreateCustomer
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Reference { get; set; }
}
