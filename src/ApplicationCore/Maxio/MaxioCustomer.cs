namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// The Maxio customer record linked to an eShopOnWeb account via its <c>reference</c>
/// (the account's username/email).
/// </summary>
public class MaxioCustomer
{
    public int Id { get; init; }
    public required string Reference { get; init; }
    public required string Email { get; init; }
}
