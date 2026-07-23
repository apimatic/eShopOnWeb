using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    /// <summary>How many units were consumed. Must be greater than zero.</summary>
    public decimal Quantity { get; set; }

    /// <summary>An optional note recorded alongside the usage.</summary>
    public string? Memo { get; set; }

    /// <summary>Administrators only: the user whose subscription the usage accrues to.</summary>
    public string? OnBehalfOfUserName { get; set; }

    /// <summary>Resolved from the bearer token; never supplied by the caller.</summary>
    [JsonIgnore]
    public string? UserName { get; set; }
}
