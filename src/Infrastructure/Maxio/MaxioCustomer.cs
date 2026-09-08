namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A Maxio customer. Mirrors the subset of <c>Customer.yaml</c> (Maxio OpenAPI spec) that this
/// integration reads and writes.
/// </summary>
public class MaxioCustomer
{
    public long Id { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    public string? Organization { get; set; }

    /// <summary>The application-provided unique reference for the customer (= the eShop shopper's email).</summary>
    public string? Reference { get; set; }
}
