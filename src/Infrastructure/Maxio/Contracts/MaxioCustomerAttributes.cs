namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>The customer attributes Maxio accepts on create. First/last name and email are required.</summary>
public class MaxioCustomerAttributes
{
    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    /// <summary>Our stable identifier for this customer. Maxio enforces one customer per reference value.</summary>
    public string? Reference { get; set; }
}
