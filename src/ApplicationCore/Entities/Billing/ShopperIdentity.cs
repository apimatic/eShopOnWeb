namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

public sealed class ShopperIdentity
{
    public required string BuyerId { get; init; }
    public required string Email { get; init; }
    public string? UserName { get; init; }
}
