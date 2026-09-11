using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Services.Maxio;

public class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }
    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }
    [JsonPropertyName("email")]
    public string? Email { get; set; }
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}
