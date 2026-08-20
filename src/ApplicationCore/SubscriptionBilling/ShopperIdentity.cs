namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

public sealed class ShopperIdentity
{
    public required string UserId { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
}
