using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Twilio.Models;

/// <summary>Mirrors the <c>LookupResponse</c> schema from the Twilio Lookups v2 OpenAPI spec.</summary>
public class TwilioLookupResponse
{
    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("validation_errors")]
    public List<string>? ValidationErrors { get; set; }
}
