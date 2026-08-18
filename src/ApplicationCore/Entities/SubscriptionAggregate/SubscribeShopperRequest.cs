namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class SubscribeShopperRequest
{
    public string UserId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string? ProductHandle { get; init; }
}
