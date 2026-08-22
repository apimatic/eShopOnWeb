using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}
