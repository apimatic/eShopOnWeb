using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record NetTerms
{
    [JsonPropertyName("default_net_terms")]
    public int? DefaultNetTerms { get; init; } = 0;

    [JsonPropertyName("automatic_net_terms")]
    public int? AutomaticNetTerms { get; init; } = 0;

    [JsonPropertyName("remittance_net_terms")]
    public int? RemittanceNetTerms { get; init; } = 0;

    [JsonPropertyName("net_terms_on_remittance_signups_enabled")]
    public bool? NetTermsOnRemittanceSignupsEnabled { get; init; } = false;

    [JsonPropertyName("custom_net_terms_enabled")]
    public bool? CustomNetTermsEnabled { get; init; } = false;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
