using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>Maxio <c>Customer Response</c> envelope.</summary>
public class CustomerResponse
{
    public Customer? Customer { get; set; }
}

/// <summary>Maxio <c>Customer</c> schema (subset consumed by this integration).</summary>
public class Customer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }

    /// <summary>The unique identifier used within the calling application for this customer.</summary>
    public string? Reference { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Maxio <c>Create Customer Request</c> body.</summary>
public class CreateCustomerRequest
{
    public CreateCustomerRequest(CreateCustomer customer) => Customer = customer;

    public CreateCustomer Customer { get; set; }
}

/// <summary>Maxio <c>Create Customer</c> schema. <c>first_name</c>, <c>last_name</c> and <c>email</c> are required.</summary>
public class CreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? Organization { get; set; }
    public string? Locale { get; set; }
}
