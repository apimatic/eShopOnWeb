using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Messaging;

internal sealed class TwilioApiErrorBody
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("more_info")]
    public string? MoreInfo { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }
}
