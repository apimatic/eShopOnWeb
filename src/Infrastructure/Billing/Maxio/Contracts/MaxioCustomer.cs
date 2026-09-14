using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>The specification's <c>Customer-Response</c> schema.</summary>
public class CustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>
/// The specification's <c>Customer</c> schema, limited to the fields this integration consumes.
/// </summary>
public class MaxioCustomer
{
    public long Id { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    public string? Organization { get; set; }

    /// <summary>The unique identifier of the customer in the calling application.</summary>
    public string? Reference { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>The specification's <c>Create-Customer-Request</c> schema.</summary>
public class CreateCustomerRequest
{
    public CreateCustomer Customer { get; set; } = new();
}

/// <summary>
/// The specification's <c>Create-Customer</c> schema. <c>first_name</c>, <c>last_name</c> and
/// <c>email</c> are required; the remaining properties are omitted when not supplied.
/// </summary>
public class CreateCustomer
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Organization { get; set; }

    public string? Reference { get; set; }
}
