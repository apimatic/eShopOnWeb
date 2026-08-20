namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

public class SubscribeToPlanRequest
{
    public string ShopperUserId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string ProductHandle { get; init; } = string.Empty;
}
