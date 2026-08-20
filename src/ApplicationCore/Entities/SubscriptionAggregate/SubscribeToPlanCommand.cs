namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class SubscribeToPlanCommand
{
    public SubscribeToPlanCommand(string shopperIdentity, string email, string firstName, string lastName, string productHandle)
    {
        ShopperIdentity = shopperIdentity;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        ProductHandle = productHandle;
    }

    public string ShopperIdentity { get; }
    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }
    public string ProductHandle { get; }
}
