using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ListSubscriptionComponentsFilter
{
    /// <summary>
    /// Allows fetching components allocation with matching currency based on provided values. Use in query <c>filter[currencies]=EUR,USD</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("currencies")]
    [MinLength(1)]
    public IReadOnlyList<string>? Currencies { get; init; }

    /// <summary>
    /// Allows fetching components allocation with matching use_site_exchange_rate based on provided value. Use in query <c>filter[use_site_exchange_rate]=true</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("use_site_exchange_rate")]
    public bool? UseSiteExchangeRate { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
