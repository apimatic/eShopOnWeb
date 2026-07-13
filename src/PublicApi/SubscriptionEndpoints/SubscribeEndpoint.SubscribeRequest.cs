using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeBody
{
    [Required]
    public string FirstName { get; set; } = string.Empty;
    [Required]
    public string LastName { get; set; } = string.Empty;
    [Required]
    public string ProductHandle { get; set; } = string.Empty;
}

public class SubscribeRequest : BaseRequest
{
    public SubscribeRequest(string customerReference, string email, SubscribeBody body)
    {
        CustomerReference = customerReference;
        Email = email;
        FirstName = body.FirstName;
        LastName = body.LastName;
        ProductHandle = body.ProductHandle;
    }

    [JsonIgnore]
    public string CustomerReference { get; }
    [JsonIgnore]
    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }
    public string ProductHandle { get; }
}
