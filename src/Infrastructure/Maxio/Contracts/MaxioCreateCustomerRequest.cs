namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>
/// Body for POST /customers.json. The uniqueness token sits beside the resource, not inside it,
/// as described by Maxio's duplicate-prevention guidance.
/// </summary>
public class MaxioCreateCustomerRequest
{
    public MaxioCustomerAttributes Customer { get; set; } = new();

    public string? UniquenessToken { get; set; }
}
