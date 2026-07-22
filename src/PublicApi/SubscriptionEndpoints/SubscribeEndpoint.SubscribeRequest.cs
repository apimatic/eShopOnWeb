namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// The plan to enrol in, given as its stable handle (for example <c>eshop-pro</c>) or as the
    /// provider's numeric plan identifier.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    public static SubscribeRequest From(SubscriptionRequestBody body) => new()
    {
        PlanHandle = body.GetString(SubscriptionRequestParser.PlanNames) ?? string.Empty
    };
}
