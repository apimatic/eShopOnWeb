using System.Collections.Generic;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation.Attributes;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record TeamThreatProtectionRequest
{
    /// <summary>
    /// Threat protection mode. <c>off</c> disables checks; <c>normal</c> checks URLs against Google Web Risk (+2 credits per URL scanned).
    /// </summary>
    [JsonPropertyName("mode")]
    public required Mode6 Mode { get; init; }

    /// <summary>
    /// Normalized score (0-100) at or above which a classifier verdict is blocked. Lower is stricter.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("riskScoreThreshold")]
    [Minimum(0)]
    [Maximum(100)]
    public int? RiskScoreThreshold { get; init; }

    /// <summary>
    /// Exact domains or globs (e.g. <c>*.example.com</c>) always blocked, without a classifier call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("blacklist")]
    public IReadOnlyList<string>? Blacklist { get; init; }

    /// <summary>
    /// Exact domains or globs always allowed. Wins over every other rule.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("whitelist")]
    public IReadOnlyList<string>? Whitelist { get; init; }

    /// <summary>
    /// Top-level domains to block outright, lowercase without a leading dot.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("blockedTlds")]
    public IReadOnlyList<string>? BlockedTlds { get; init; }

    /// <summary>
    /// Behavior when the classifier is unreachable: <c>closed</c> blocks (default), <c>open</c> allows.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("failurePolicy")]
    public FailurePolicy1? FailurePolicy { get; init; }

    /// <summary>
    /// Whether individual requests may pass a <c>threatProtection</c> object. When false, such requests are rejected with 403.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("allowRequestOverrides")]
    public bool? AllowRequestOverrides { get; init; }
}
