using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

/// <summary>
/// Nested filter used for List Subscription Components For Site Filter
/// </summary>
public record SubscriptionFilter
{
    /// <summary>
    /// Allows fetching components allocations that belong to the subscription with matching states based on provided values. To use this filter you also have to include the following param in the request <c>include=subscription</c>. Use in query <c>filter[subscription][states]=active,canceled&amp;include=subscription</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("states")]
    [MinLength(1)]
    public IReadOnlyList<SubscriptionStateFilter>? States { get; init; }

    /// <summary>
    /// The type of filter you'd like to apply to your search. To use this filter you also have to include the following param in the request <c>include=subscription</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_field")]
    public SubscriptionListDateField? DateField { get; init; }

    /// <summary>
    /// The start date (format YYYY-MM-DD) with which to filter the date_field. Returns components that belong to the subscription with a timestamp at or after midnight (12:00:00 AM) in your site’s time zone on the date specified. To use this filter you also have to include the following param in the request <c>include=subscription</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("start_date")]
    public DateTimeOffset? StartDate { get; init; }

    /// <summary>
    /// The end date (format YYYY-MM-DD) with which to filter the date_field. Returns components that belong to the subscription with a timestamp up to and including 11:59:59PM in your site’s time zone on the date specified. To use this filter you also have to include the following param in the request <c>include=subscription</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("end_date")]
    public DateTimeOffset? EndDate { get; init; }

    /// <summary>
    /// The start date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns components that belong to the subscription with a timestamp at or after exact time provided in query. You can specify timezone in query - otherwise your site''s time zone will be used. If provided, this parameter will be used instead of start_date. To use this filter you also have to include the following param in the request <c>include=subscription</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("start_datetime")]
    public DateTimeOffset? StartDatetime { get; init; }

    /// <summary>
    /// The end date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns components that belong to the subscription with a timestamp at or before exact time provided in query. You can specify timezone in query - otherwise your site''s time zone will be used. If provided, this parameter will be used instead of end_date. To use this filter you also have to include the following param in the request <c>include=subscription</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("end_datetime")]
    public DateTimeOffset? EndDatetime { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
