namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class ShopperIdentity
{
    public ShopperIdentity(string userId, string email, string firstName, string lastName)
    {
        UserId = userId;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    public string UserId { get; }
    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }
}
