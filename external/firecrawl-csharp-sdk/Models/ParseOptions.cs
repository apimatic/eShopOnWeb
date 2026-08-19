using System.Collections.Generic;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation.Attributes;
using FirecrawlApi.Models.AnyOf;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

/// <summary>
/// Optional parse options sent as JSON in the multipart <c>options</c> field.
/// </summary>
public record ParseOptions
{
    /// <summary>
    /// Output formats supported for <c>/parse</c> uploads. Browser-rendering formats and change tracking are not supported.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("formats")]
    public IReadOnlyList<ParseFormat>? Formats { get; init; }

    /// <summary>
    /// Only return the main content of the page excluding headers, navs, footers, etc.
    /// </summary>
    [JsonPropertyName("onlyMainContent")]
    public bool? OnlyMainContent { get; init; } = true;

    /// <summary>
    /// Tags to include in the output.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("includeTags")]
    public IReadOnlyList<string>? IncludeTags { get; init; }

    /// <summary>
    /// Tags to exclude from the output.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("excludeTags")]
    public IReadOnlyList<string>? ExcludeTags { get; init; }

    /// <summary>
    /// Headers to send when additional network requests are required.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("headers")]
    public object? Headers { get; init; }

    /// <summary>
    /// Timeout in milliseconds for the request. Default is 30000 (30 seconds). Maximum is 300000 (300 seconds).
    /// </summary>
    [JsonPropertyName("timeout")]
    [Maximum(300000)]
    public int? Timeout { get; init; } = 30000;

    /// <summary>
    /// Controls file parser behavior when relevant (for example PDF parser mode).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("parsers")]
    public IReadOnlyList<Parser1>? Parsers { get; init; }

    /// <summary>
    /// Skip TLS certificate verification when making requests.
    /// </summary>
    [JsonPropertyName("skipTlsVerification")]
    public bool? SkipTlsVerification { get; init; } = true;

    /// <summary>
    /// Remove base64-encoded images from output and keep alt text placeholders.
    /// </summary>
    [JsonPropertyName("removeBase64Images")]
    public bool? RemoveBase64Images { get; init; } = true;

    /// <summary>
    /// Enable ad and cookie popup blocking.
    /// </summary>
    [JsonPropertyName("blockAds")]
    public bool? BlockAds { get; init; } = true;

    /// <summary>
    /// Redact personally identifiable information from returned markdown. Pass <c>true</c> to use defaults, or an object to tune mode, entities, and replacement style.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("redactPII")]
    public RedactPii? RedactPii { get; init; }

    /// <summary>
    /// Proxy mode for parse uploads. <c>/parse</c> supports only <c>basic</c> and <c>auto</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("proxy")]
    public Proxy1? Proxy { get; init; }

    /// <summary>
    /// Origin identifier for analytics and logging.
    /// </summary>
    [JsonPropertyName("origin")]
    public string? Origin { get; init; } = "api";

    /// <summary>
    /// Optional integration identifier.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("integration")]
    public string? Integration { get; init; }

    /// <summary>
    /// User attribution included with SIEM logging events when SIEM Logging is enabled for the organization.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("auditMetadata")]
    public AuditMetadata? AuditMetadata { get; init; }

    /// <summary>
    /// If true, this will enable zero data retention for this parse. To enable this feature, please contact help@firecrawl.dev
    /// </summary>
    [JsonPropertyName("zeroDataRetention")]
    public bool? ZeroDataRetention { get; init; } = false;
}
