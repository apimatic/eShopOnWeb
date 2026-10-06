namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// Request body for <c>POST /customers.json</c> (Create Customer), per the OpenAPI
/// spec schema <c>Create-Customer-Request</c>.
/// </summary>
public class CreateMaxioCustomerRequest
{
    public CreateMaxioCustomer Customer { get; set; } = new();
}

/// <summary>
/// Customer attributes for the Create Customer operation, per the OpenAPI spec
/// schema <c>Create-Customer</c>.
/// </summary>
public class CreateMaxioCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? CcEmails { get; set; }
    public string? Organization { get; set; }
    public string? Reference { get; set; }
    public string? Address { get; set; }
    public string? Address2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public string? Locale { get; set; }
    public string? VatNumber { get; set; }
    public bool? TaxExempt { get; set; }
    public string? TaxExemptReason { get; set; }
    public int? ParentId { get; set; }
    public string? SalesforceId { get; set; }
}
