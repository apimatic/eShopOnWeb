namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Payload for creating a Maxio customer. <see cref="Reference"/> must be unique per Maxio
/// site and is how eShopOnWeb maps a Maxio customer back to an eShopOnWeb user.
/// </summary>
public class MaxioCustomerCreate
{
    public required string Reference { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
}
