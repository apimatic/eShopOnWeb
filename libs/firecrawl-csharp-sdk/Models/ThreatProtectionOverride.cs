using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation.Attributes;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

/// <summary>
/// Per-request <see href="https://docs.firecrawl.dev/features/threat-protection">Threat Protection</see> override. Fields you provide replace the corresponding fields of your organization's policy for this request only; omitted fields keep their organization-level values. Requires Threat Protection to be enabled for your team (enterprise feature) — otherwise the request is rejected with a 403. If your organization has disabled request overrides, any request that includes this object is rejected with a 403. If Threat Protection is enforced for your team, <c>mode</c> may not be set to <c>off</c>.
/// </summary>
public record ThreatProtectionOverride
{
    /// <summary>
    /// URL scanning mode for this request. <c>normal</c> checks URLs against Google Web Risk (+2 credits per URL scanned).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("mode")]
    public Mode3? Mode { get; init; }

    /// <summary>
    /// Normalized risk score (0–100) at or above which a classifier verdict blocks the URL. Lower is stricter.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("riskScoreThreshold")]
    [Minimum(0)]
    [Maximum(100)]
    public int? RiskScoreThreshold { get; init; }

    /// <summary>
    /// Domains to always block, as plain domains (<c>example.com</c>) or wildcard globs (<c>*.example.com</c>). No protocol, path, or port.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("blacklist")]
    [MaxLength(1000)]
    public IReadOnlyList<string>? Blacklist { get; init; }

    /// <summary>
    /// Domains to always allow, as plain domains or wildcard globs. Wins over every other rule.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("whitelist")]
    [MaxLength(1000)]
    public IReadOnlyList<string>? Whitelist { get; init; }

    /// <summary>
    /// Top-level domains to block outright, lowercase without the leading dot (e.g. <c>zip</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("blockedTlds")]
    [MaxLength(1000)]
    public IReadOnlyList<string>? BlockedTlds { get; init; }

    /// <summary>
    /// What to do when the classifier can't be reached: <c>closed</c> blocks the request, <c>open</c> allows it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("failurePolicy")]
    public FailurePolicy? FailurePolicy { get; init; }
}
