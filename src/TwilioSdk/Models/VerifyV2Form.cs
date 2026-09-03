using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record VerifyV2Form
{
    /// <summary>
    /// The Type of this Form. Currently only <c>form-push</c> is supported.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("form_type")]
    public FormEnumFormTypes? FormType { get; init; }

    /// <summary>
    /// Object that contains the available forms for this type. This available forms are given in the standard <see href="https://json-schema.org/">JSON Schema</see> format
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("forms")]
    public object? Forms { get; init; }

    /// <summary>
    /// Additional information for the available forms for this type. E.g. The separator string used for <c>binding</c> in a Factor push.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("form_meta")]
    public object? FormMeta { get; init; }

    /// <summary>
    /// The URL to access the forms for this type.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
