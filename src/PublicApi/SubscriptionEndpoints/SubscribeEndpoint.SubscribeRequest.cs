namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    public string UserId { get; }
    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }
    public string PlanHandle { get; }

    public SubscribeRequest(string userId, string email, string firstName, string lastName, string planHandle)
    {
        UserId = userId;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        PlanHandle = planHandle;
    }
}
