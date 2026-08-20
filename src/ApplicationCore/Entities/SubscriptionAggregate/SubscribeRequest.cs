namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class SubscribeRequest
{
    public SubscribeRequest(string userId, string email, string firstName, string lastName, string? productHandle)
    {
        UserId = userId;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        ProductHandle = productHandle;
    }

    public string UserId { get; }
    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }
    public string? ProductHandle { get; }
}
