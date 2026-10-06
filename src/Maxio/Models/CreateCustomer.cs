namespace Maxio.Models;

/// <summary>
/// The customer payload used when creating a customer. Mirrors the Create Customer schema in the Maxio OpenAPI specification.
/// </summary>
public class CreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
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
}
