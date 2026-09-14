using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public MySubscriptionsRequest(string? userName)
    {
        UserName = userName;
    }

    /// <summary>The subscriber's identity, taken from the JWT (never from client input).</summary>
    [JsonIgnore]
    public string? UserName { get; }
}
