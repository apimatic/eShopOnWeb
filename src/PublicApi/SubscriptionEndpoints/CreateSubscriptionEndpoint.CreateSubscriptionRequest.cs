namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan to subscribe to, as returned by api/subscription-plans. Required.
    /// </summary>
    public string PlanHandle { get; set; }

    /// <summary>
    /// Optional first name for the billing customer, used only the first time a customer is created
    /// for this account. Defaults to the local part of the account's email address.
    /// </summary>
    public string FirstName { get; set; }

    /// <summary>
    /// Optional last name for the billing customer, used only the first time a customer is created
    /// for this account.
    /// </summary>
    public string LastName { get; set; }
}
