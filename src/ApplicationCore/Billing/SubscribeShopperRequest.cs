namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public class SubscribeShopperRequest
{
    public string BuyerId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? UserName { get; init; }
    public string ProductHandle { get; init; } = string.Empty;
}
