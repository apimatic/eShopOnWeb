using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record MessagingV2ChannelsSenderResponse
{
    /// <summary>
    /// The SID of the sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^XE[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The status of the sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public ChannelsSenderEnumStatus? Status { get; init; }

    /// <summary>
    /// The ID of the sender in <c>whatsapp:&lt;E.164_PHONE_NUMBER&gt;</c> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sender_id")]
    public string? SenderId { get; init; }

    /// <summary>
    /// The configuration settings for creating a sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("configuration")]
    public MessagingV2ChannelsSenderConfiguration? Configuration { get; init; }

    /// <summary>
    /// The configuration settings for webhooks.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("webhook")]
    public MessagingV2ChannelsSenderWebhook? Webhook { get; init; }

    /// <summary>
    /// The profile information for the sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("profile")]
    public MessagingV2ChannelsSenderProfileGenericResponse? Profile { get; init; }

    /// <summary>
    /// The additional properties for the sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("properties")]
    public MessagingV2ChannelsSenderProperties? Properties { get; init; }

    /// <summary>
    /// The reasons why the sender is offline.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("offline_reasons")]
    public IReadOnlyList<MessagingV2ChannelsSenderOfflineReasonsItems?>? OfflineReasons { get; init; }

    /// <summary>
    /// The KYC compliance information. This section consists of response to the request launch.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("compliance")]
    public MessagingV2RcsComplianceResponse? Compliance { get; init; }

    /// <summary>
    /// The URL of the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
