namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

public class SubscribeRequest
{
    public SubscribeRequest(string userId, string email, string userName, string productHandle)
    {
        UserId = userId;
        Email = email;
        UserName = userName;
        ProductHandle = productHandle;
    }

    public string UserId { get; }
    public string Email { get; }
    public string UserName { get; }
    public string ProductHandle { get; }
}
