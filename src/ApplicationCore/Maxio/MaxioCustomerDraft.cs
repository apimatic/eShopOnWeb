namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// The data required to create a customer in Maxio Advanced Billing.
/// </summary>
public class MaxioCustomerDraft
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
}
