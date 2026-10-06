namespace Maxio.Models;

/// <summary>
/// A Maxio Advanced Billing customer. Mirrors the Customer schema in the Maxio OpenAPI specification.
/// </summary>
public class Customer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }
    public string? Reference { get; set; }
    public string? Address { get; set; }
    public string? Address2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public bool TaxExempt { get; set; }
    public string? VatNumber { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
