using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderByIdRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }
}
