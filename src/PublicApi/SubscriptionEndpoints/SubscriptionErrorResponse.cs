using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionErrorResponse
{
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>Provider validation messages, when the billing provider refused the request.</summary>
    public List<string> Errors { get; set; } = new();
}
