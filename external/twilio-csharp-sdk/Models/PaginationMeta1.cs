using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// Pagination metadata containing navigation tokens and result information,
/// this schema should according to convention be added to the response
/// payload's 'meta' attribute
/// </summary>
public record PaginationMeta1
{
    /// <summary>
    /// The key of the list property contains the actual data items.
    /// This enables programmatic iteration over paginated results.
    /// </summary>
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    /// <summary>
    /// The actual number of items returned in this response.
    /// May be less than the requested pageSize for the last page.
    /// </summary>
    [JsonPropertyName("pageSize")]
    public required int PageSize { get; init; }

    /// <summary>
    /// Token to fetch the previous page of results.
    /// Only included if there is a previous page, otherwise omitted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("previousToken")]
    public string? PreviousToken { get; init; }

    /// <summary>
    /// Token to fetch the next page of results.
    /// Only included if there is a next page, otherwise omitted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("nextToken")]
    public string? NextToken { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
