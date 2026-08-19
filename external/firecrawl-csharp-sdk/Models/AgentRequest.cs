using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record AgentRequest
{
    /// <summary>
    /// Optional list of URLs to constrain the agent to
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("urls")]
    public IReadOnlyList<string>? Urls { get; init; }

    /// <summary>
    /// The prompt describing what data to extract
    /// </summary>
    [JsonPropertyName("prompt")]
    [MaxLength(10000)]
    public required string Prompt { get; init; }

    /// <summary>
    /// Optional JSON schema to structure the extracted data
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("schema")]
    public object? Schema { get; init; }

    /// <summary>
    /// Maximum credits to spend on this agent task. Defaults to 2500 if not set. Values above 2,500 are always billed as paid requests.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("maxCredits")]
    public double? MaxCredits { get; init; }

    /// <summary>
    /// If true, agent will only visit URLs provided in the urls array
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("strictConstrainToURLs")]
    public bool? StrictConstrainToUrLs { get; init; }

    /// <summary>
    /// The model to use for the agent task. spark-1-mini (default) is 60% cheaper, spark-1-pro offers higher accuracy for complex tasks
    /// </summary>
    [JsonPropertyName("model")]
    public Model? Model { get; init; } = Model.Spark1Mini;

    /// <summary>
    /// User attribution included with SIEM logging events when SIEM Logging is enabled for the organization.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("auditMetadata")]
    public AuditMetadata? AuditMetadata { get; init; }

    /// <summary>
    /// Per-request <see href="https://docs.firecrawl.dev/features/threat-protection">Threat Protection</see> override. Fields you provide replace the corresponding fields of your organization's policy for this request only; omitted fields keep their organization-level values. Requires Threat Protection to be enabled for your team (enterprise feature) — otherwise the request is rejected with a 403. If your organization has disabled request overrides, any request that includes this object is rejected with a 403. If Threat Protection is enforced for your team, <c>mode</c> may not be set to <c>off</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("threatProtection")]
    public ThreatProtectionOverride? ThreatProtection { get; init; }
}
