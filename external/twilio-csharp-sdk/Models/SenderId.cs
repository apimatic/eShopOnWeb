using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record SenderId
{
    /// <summary>
    /// Account that owns the Sender ID.
    /// </summary>
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public required string AccountSid { get; init; }

    /// <summary>
    /// The date and time when the Sender ID was created.
    /// </summary>
    [JsonPropertyName("date_created")]
    public required DateTimeOffset DateCreated { get; init; }

    /// <summary>
    /// The date and time when the Sender ID was last updated.
    /// </summary>
    [JsonPropertyName("date_updated")]
    public required DateTimeOffset DateUpdated { get; init; }

    /// <summary>
    /// The alphanumeric sender ID.
    /// </summary>
    [JsonPropertyName("sender_id")]
    public required string SenderIdValue { get; init; }

    /// <summary>
    /// The unique identifier of the Sender ID.
    /// </summary>
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^SU[0-9a-fA-F]{32}$")]
    public required string Sid { get; init; }

    /// <summary>
    /// Messages per second (throughput) for the Sender ID.
    /// </summary>
    [JsonPropertyName("mps")]
    public required int Mps { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
