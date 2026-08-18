namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public string From { get; init; }
    public string To { get; init; }

    public ReconciliationRequest(string from, string to)
    {
        From = from;
        To = to;
    }
}
