namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public class SubscribeToPlanRequest
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
}
