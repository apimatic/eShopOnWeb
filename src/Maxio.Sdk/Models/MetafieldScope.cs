using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

/// <summary>
/// Warning: When updating a metafield's scope attribute, all scope attributes must be passed. Partially complete scope attributes will override the existing settings.
/// </summary>
public record MetafieldScope
{
    /// <summary>
    /// Include (1) or exclude (0) metafields from the csv export.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("csv")]
    public IncludeOption? Csv { get; init; }

    /// <summary>
    /// Include (1) or exclude (0) metafields from invoices.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("invoices")]
    public IncludeOption? Invoices { get; init; }

    /// <summary>
    /// Include (1) or exclude (0) metafields from statements.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("statements")]
    public IncludeOption? Statements { get; init; }

    /// <summary>
    /// Include (1) or exclude (0) metafields from the portal.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("portal")]
    public IncludeOption? Portal { get; init; }

    /// <summary>
    /// Include (1) or exclude (0) metafields used in <see href="page:development-tools/embeddable-components/overview">Embeddable Components</see> from being viewable by your ecosystem.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("public_show")]
    public IncludeOption? PublicShow { get; init; }

    /// <summary>
    /// Include (1) or exclude (0) metafields used in <see href="page:development-tools/embeddable-components/overview">Embeddable Components</see> from being editable by your ecosystem.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("public_edit")]
    public IncludeOption? PublicEdit { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("hosted")]
    public IReadOnlyList<string>? Hosted { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
