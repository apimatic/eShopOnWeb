using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints.ContactNumbers;

public class CreateContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number to register (any format the provider can canonicalize).</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Set from the token — never bound from the request body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}
