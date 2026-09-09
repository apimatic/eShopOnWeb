using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A customer in Maxio Advanced Billing (the API's customer object).
/// </summary>
public class MaxioCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public string? Reference { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
