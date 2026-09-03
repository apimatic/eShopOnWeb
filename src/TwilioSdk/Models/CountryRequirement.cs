using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record CountryRequirement
{
    /// <summary>
    /// Iso country code as per ISO 3166-1 alpha-2 standard
    /// </summary>
    [JsonPropertyName("iso_country")]
    [StringLength(2, MinimumLength = 2)]
    public required string IsoCountry { get; init; }

    /// <summary>
    /// Whether Sender ID needs to be pre-registered for the country
    /// </summary>
    [JsonPropertyName("registration_required")]
    public required bool RegistrationRequired { get; init; }

    /// <summary>
    /// Twilio SLA for Sender Id Registration process in business days. For countries requiring dynamic registration, it will be set to 0.
    /// </summary>
    [JsonPropertyName("sla_in_days")]
    public required int SlaInDays { get; init; }

    /// <summary>
    /// Whether promotional usage for Sender ID is supported
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("promotional_supported")]
    public bool? PromotionalSupported { get; init; }

    /// <summary>
    /// Mandatory prefix string for Sender ID when used for promotional purpose in the country
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("promotional_sender_id_prefix")]
    public string? PromotionalSenderIdPrefix { get; init; }

    /// <summary>
    /// Mandatory suffix string for Sender ID when used for promotional purpose in the country
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("promotional_sender_id_suffix")]
    public string? PromotionalSenderIdSuffix { get; init; }

    /// <summary>
    /// Represents pricing requirements for country with free-flowing string format
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("pricing_scheme")]
    public string? PricingScheme { get; init; }

    /// <summary>
    /// Represents public Twilio support URL which has information regarding the instructions and documents required for registration
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("documentation_url")]
    public string? DocumentationUrl { get; init; }

    /// <summary>
    /// Represents the Twilio public URL for documentation template required to be filled for the Sender ID registration
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("documentation_template_url")]
    public string? DocumentationTemplateUrl { get; init; }

    /// <summary>
    /// List of document type machine names
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("document_type_machine_names")]
    public IReadOnlyList<string>? DocumentTypeMachineNames { get; init; }

    /// <summary>
    /// List of document type machine names for Domestic traffic reach
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("domestic_document_type_machine_names")]
    public IReadOnlyList<string>? DomesticDocumentTypeMachineNames { get; init; }

    /// <summary>
    /// List of document type machine names for International traffic reach
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("international_document_type_machine_names")]
    public IReadOnlyList<string>? InternationalDocumentTypeMachineNames { get; init; }

    /// <summary>
    /// Sender ID string rules for the country
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sender_id_registration_rules")]
    public string? SenderIdRegistrationRules { get; init; }

    /// <summary>
    /// Whether USO (Unified Sender Onboarding) is enabled for this country
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uso_enabled")]
    public bool? UsoEnabled { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
