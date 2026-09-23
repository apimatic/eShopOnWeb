using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record FlexV1PluginConfigurationConfiguredPlugin
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that the Flex Plugin resource is installed for.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The SID of the Flex Plugin Configuration that this Flex Plugin belongs to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("configuration_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^FJ[0-9a-fA-F]{32}$")]
    public string? ConfigurationSid { get; init; }

    /// <summary>
    /// The SID of the Flex Plugin.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("plugin_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^FP[0-9a-fA-F]{32}$")]
    public string? PluginSid { get; init; }

    /// <summary>
    /// The SID of the Flex Plugin Version.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("plugin_version_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^FV[0-9a-fA-F]{32}$")]
    public string? PluginVersionSid { get; init; }

    /// <summary>
    /// The phase this Flex Plugin would initialize at runtime.
    /// </summary>
    [JsonPropertyName("phase")]
    public int? Phase { get; init; } = 0;

    /// <summary>
    /// The URL of where the Flex Plugin Version JavaScript bundle is hosted on.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("plugin_url")]
    public string? PluginUrl { get; init; }

    /// <summary>
    /// The name that uniquely identifies this Flex Plugin resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unique_name")]
    public string? UniqueName { get; init; }

    /// <summary>
    /// The friendly name of this Flex Plugin resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// A descriptive string that you create to describe the plugin resource. It can be up to 500 characters long
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Whether the Flex Plugin is archived. The default value is false.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("plugin_archived")]
    public bool? PluginArchived { get; init; }

    /// <summary>
    /// The latest version of this Flex Plugin Version.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>
    /// A changelog that describes the changes this Flex Plugin Version brings.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("changelog")]
    public string? Changelog { get; init; }

    /// <summary>
    /// Whether the Flex Plugin Version is archived. The default value is false.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("plugin_version_archived")]
    public bool? PluginVersionArchived { get; init; }

    /// <summary>
    /// Whether to validate the request is authorized to access the Flex Plugin Version.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("private")]
    public bool? Private { get; init; }

    /// <summary>
    /// The date and time in GMT when the Flex Plugin was installed specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The absolute URL of the Flex Plugin resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
