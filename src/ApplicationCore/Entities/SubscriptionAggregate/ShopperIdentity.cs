namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The authenticated eShopOnWeb user that should map 1:1 to a Maxio customer via <see cref="UserId"/>.
/// </summary>
public class ShopperIdentity
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}
