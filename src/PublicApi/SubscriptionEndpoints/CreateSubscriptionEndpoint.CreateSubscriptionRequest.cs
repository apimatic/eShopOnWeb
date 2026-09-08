namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request body for POST api/subscriptions. Only <see cref="PlanHandle"/> is required;
/// <see cref="FirstName"/> and <see cref="LastName"/> are captured on the Maxio customer
/// when it is first created for the caller (a name is derived from the account email when
/// they are omitted).
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;

    public string? FirstName { get; set; }

    public string? LastName { get; set; }
}
