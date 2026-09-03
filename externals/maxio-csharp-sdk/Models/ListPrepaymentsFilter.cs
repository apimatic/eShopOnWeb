using System;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record ListPrepaymentsFilter
{
    /// <summary>
    /// The type of filter you would like to apply to your search. <c>created_at</c> - Time when prepayment was created. <c>application_at</c> - Time when prepayment was applied to invoice. Use in query <c>filter[date_field]=created_at</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_field")]
    public ListPrepaymentDateField? DateField { get; init; }

    /// <summary>
    /// The start date (format YYYY-MM-DD) with which to filter the date_field. Returns prepayments with a timestamp at or after midnight (12:00:00 AM) in your site's time zone on the date specified. Use in query: <c>filter[start_date]=2011-12-15</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("start_date")]
    public DateTimeOffset? StartDate { get; init; }

    /// <summary>
    /// The end date (format YYYY-MM-DD) with which to filter the date_field. Returns prepayments with a timestamp up to and including 11:59:59PM in your site's time zone on the date specified. Use in query: <c>filter[end_date]=2011-12-15</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("end_date")]
    public DateTimeOffset? EndDate { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
