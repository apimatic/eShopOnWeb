using System;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record SenderIdCountry
{
    /// <summary>
    /// The unique identifier of the Sender ID Country.
    /// </summary>
    [JsonPropertyName("routing_table_sid")]
    public required string RoutingTableSid { get; init; }

    /// <summary>
    /// The ISO country code.
    /// </summary>
    [JsonPropertyName("iso_country")]
    public required string IsoCountry { get; init; }

    /// <summary>
    /// The date and time when the country routing table was created.
    /// </summary>
    [JsonPropertyName("date_created")]
    public required DateTimeOffset DateCreated { get; init; }

    /// <summary>
    /// The date and time when the country routing table was last updated.
    /// </summary>
    [JsonPropertyName("date_updated")]
    public required DateTimeOffset DateUpdated { get; init; }

    /// <summary>
    /// Indicates if this is the default routing table for the country.
    /// </summary>
    [JsonPropertyName("default")]
    public required bool Default { get; init; }

    /// <summary>
    /// The status of the country for the sender Id
    /// </summary>
    [JsonPropertyName("status")]
    public required Status Status { get; init; }

    /// <summary>
    /// The override status of the country for the sender Id
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status_override_info")]
    public StatusOverrideInfo? StatusOverrideInfo { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
