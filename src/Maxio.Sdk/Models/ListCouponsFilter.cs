using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record ListCouponsFilter
{
    /// <summary>
    /// The type of filter you would like to apply to your search. Use in query <c>filter[date_field]=created_at</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_field")]
    public BasicDateField? DateField { get; init; }

    /// <summary>
    /// The start date (format YYYY-MM-DD) with which to filter the date_field. Returns coupons with a timestamp at or after midnight (12:00:00 AM) in your site’s time zone on the date specified. Use in query <c>filter[start_date]=2011-12-17</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("start_date")]
    public DateTimeOffset? StartDate { get; init; }

    /// <summary>
    /// The end date (format YYYY-MM-DD) with which to filter the date_field. Returns coupons with a timestamp up to and including 11:59:59PM in your site’s time zone on the date specified. Use in query <c>filter[end_date]=2011-12-15</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("end_date")]
    public DateTimeOffset? EndDate { get; init; }

    /// <summary>
    /// The start date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns coupons with a timestamp at or after exact time provided in query. You can specify timezone in query - otherwise your site's time zone will be used. If provided, this parameter will be used instead of start_date. Use in query <c>filter[start_datetime]=2011-12-19T10:15:30+01:00</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("start_datetime")]
    public DateTimeOffset? StartDatetime { get; init; }

    /// <summary>
    /// The end date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns coupons with a timestamp at or before exact time provided in query. You can specify timezone in query - otherwise your site's time zone will be used. If provided, this parameter will be used instead of end_date. Use in query <c>filter[end_datetime]=2011-12-1T10:15:30+01:00</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("end_datetime")]
    public DateTimeOffset? EndDatetime { get; init; }

    /// <summary>
    /// Allows fetching coupons with matching id based on provided values. Use in query <c>filter[ids]=1,2,3</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ids")]
    [MinLength(1)]
    public IReadOnlyList<int>? Ids { get; init; }

    /// <summary>
    /// Allows fetching coupons with matching codes based on provided values. Use in query <c>filter[codes]=free,free_trial</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("codes")]
    public IReadOnlyList<string>? Codes { get; init; }

    /// <summary>
    /// If true, restricts the list to coupons whose pricing is recalculated from the site’s current exchange rates, so their currency_prices array contains on-the-fly conversions rather than stored price records. If false, restricts the list to coupons that have manually defined amounts for each currency, ensuring the response includes the saved currency_prices entries instead of exchange-rate-derived values. Use in query <c>filter[use_site_exchange_rate]=true</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("use_site_exchange_rate")]
    public bool? UseSiteExchangeRate { get; init; }

    /// <summary>
    /// Controls returning archived coupons.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("include_archived")]
    public bool? IncludeArchived { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
