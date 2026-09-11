using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Dto;

public class MaxioErrorResponse
{
    [JsonPropertyName("errors")]
    public object? Errors { get; set; }
}
