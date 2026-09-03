using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record ApiV2010AccountUsageUsageRecordUsageRecordYearly
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that accrued the usage.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The API version used to create the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("api_version")]
    public string? ApiVersion { get; init; }

    /// <summary>
    /// Usage records up to date as of this timestamp, formatted as YYYY-MM-DDTHH:MM:SS+00:00. All timestamps are in GMT
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("as_of")]
    public string? AsOf { get; init; }

    /// <summary>
    /// The category of usage. For more information, see <see href="https://www.twilio.com/docs/usage/api/usage-record#usage-categories">Usage Categories</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("category")]
    public string? Category { get; init; }

    /// <summary>
    /// The number of usage events, such as the number of calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("count")]
    public string? Count { get; init; }

    /// <summary>
    /// The units in which <c>count</c> is measured, such as <c>calls</c> for calls or <c>messages</c> for SMS.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("count_unit")]
    public string? CountUnit { get; init; }

    /// <summary>
    /// A plain-language description of the usage category.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// The last date for which usage is included in the UsageRecord. The date is specified in GMT and formatted as <c>YYYY-MM-DD</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("end_date")]
    public DateTimeOffset? EndDate { get; init; }

    /// <summary>
    /// The total price of the usage in the currency specified in <c>price_unit</c> and associated with the account.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price")]
    public double? Price { get; init; }

    /// <summary>
    /// The currency in which <c>price</c> is measured, in <see href="https://www.iso.org/iso/home/standards/currency_codes.htm">ISO 4127</see> format, such as <c>usd</c>, <c>eur</c>, and <c>jpy</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price_unit")]
    public string? PriceUnit { get; init; }

    /// <summary>
    /// The first date for which usage is included in this UsageRecord. The date is specified in GMT and formatted as <c>YYYY-MM-DD</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("start_date")]
    public DateTimeOffset? StartDate { get; init; }

    /// <summary>
    /// A list of related resources identified by their URIs. For more information, see <see href="https://www.twilio.com/docs/usage/api/usage-record#list-subresources">List Subresources</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subresource_uris")]
    public object? SubresourceUris { get; init; }

    /// <summary>
    /// The URI of the resource, relative to <c>https://api.twilio.com</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    /// <summary>
    /// The amount used to bill usage and measured in units described in <c>usage_unit</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("usage")]
    public string? Usage { get; init; }

    /// <summary>
    /// The units in which <c>usage</c> is measured, such as <c>minutes</c> for calls or <c>messages</c> for SMS.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("usage_unit")]
    public string? UsageUnit { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
