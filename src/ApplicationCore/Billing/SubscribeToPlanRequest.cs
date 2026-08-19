namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public class SubscribeToPlanRequest
{
    public required string CustomerReference { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string ProductHandle { get; init; }
}
