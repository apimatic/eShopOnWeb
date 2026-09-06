namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansRequest : BaseRequest
{
    public ListSubscriptionPlansRequest(bool includeComponents)
    {
        IncludeComponents = includeComponents;
    }

    /// <summary>
    /// Also return the add-on components offered alongside the plans. Off by default so the common case
    /// costs one call to the billing system rather than two.
    /// </summary>
    public bool IncludeComponents { get; }
}
