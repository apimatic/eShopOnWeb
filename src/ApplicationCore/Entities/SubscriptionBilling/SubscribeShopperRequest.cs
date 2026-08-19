namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

public class SubscribeShopperRequest
{
    public required string ShopperUserId { get; init; }
    public required string Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public required string ProductHandle { get; init; }
}
