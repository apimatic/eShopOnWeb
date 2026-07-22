namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    /// <summary>How many units were consumed. Must be greater than zero.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Optional note stored alongside the usage record.</summary>
    public string? Memo { get; set; }

    public static RecordUsageRequest From(SubscriptionRequestBody body) => new()
    {
        Quantity = body.GetDecimal(SubscriptionRequestParser.QuantityNames) ?? 0m,
        Memo = body.GetString(SubscriptionRequestParser.MemoNames)
    };
}
