using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record FlexV1FlexFlow
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Flex Flow resource and owns this Workflow.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was created specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was last updated specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// The unique string that we created to identify the Flex Flow resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^FO[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The string that you assigned to describe the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// The SID of the chat service.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("chat_service_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^IS[0-9a-fA-F]{32}$")]
    public string? ChatServiceSid { get; init; }

    /// <summary>
    /// The channel type. One of <c>web</c>, <c>facebook</c>, <c>sms</c>, <c>whatsapp</c>, <c>line</c> or <c>custom</c>. By default, Studio’s Send to Flex widget passes it on to the Task attributes for Tasks created based on this Flex Flow. The Task attributes will be used by the Flex UI to render the respective Task as appropriate (applying channel-specific design and length limits). If <c>channelType</c> is <c>facebook</c>, <c>whatsapp</c> or <c>line</c>, the Send to Flex widget should set the Task Channel to Programmable Chat.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channel_type")]
    public FlexFlowEnumChannelType? ChannelType { get; init; }

    /// <summary>
    /// The channel contact's Identity.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("contact_identity")]
    public string? ContactIdentity { get; init; }

    /// <summary>
    /// Whether the Flex Flow is enabled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    /// <summary>
    /// The software that will handle inbound messages. <see href="https://www.twilio.com/docs/flex/developer/messaging/manage-flows#integration-types">Integration Type</see> can be: <c>studio</c>, <c>external</c>,  or <c>task</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("integration_type")]
    public FlexFlowEnumIntegrationType? IntegrationType { get; init; }

    /// <summary>
    /// An object that contains specific parameters for the integration.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("integration")]
    public object? Integration { get; init; }

    /// <summary>
    /// When enabled, Flex will keep the chat channel active so that it may be used for subsequent interactions with a contact identity. Defaults to <c>false</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("long_lived")]
    public bool? LongLived { get; init; }

    /// <summary>
    /// When enabled, the Messaging Channel Janitor will remove active Proxy sessions if the associated Task is deleted outside of the Flex UI. Defaults to <c>false</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("janitor_enabled")]
    public bool? JanitorEnabled { get; init; }

    /// <summary>
    /// The absolute URL of the Flex Flow resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
