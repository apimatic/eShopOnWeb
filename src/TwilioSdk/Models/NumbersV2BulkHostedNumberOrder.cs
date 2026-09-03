using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record NumbersV2BulkHostedNumberOrder
{
    /// <summary>
    /// A 34 character string that uniquely identifies this BulkHostedNumberOrder.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bulk_hosting_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BH[0-9a-fA-F]{32}$")]
    public string? BulkHostingSid { get; init; }

    /// <summary>
    /// A string that shows the status of the current Bulk Hosting request, it can vary between these values: 'QUEUED','IN_PROGRESS','PROCESSED'
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("request_status")]
    public BulkHostedNumberOrderEnumRequestStatus? RequestStatus { get; init; }

    /// <summary>
    /// A 128 character string that is a human-readable text that describes this resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// Email address used for send notifications about this Bulk hosted number request.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("notification_email")]
    public string? NotificationEmail { get; init; }

    /// <summary>
    /// The date this resource was created, given as <see href="http://www.ietf.org/rfc/rfc2822.txt">GMT RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date that this resource was completed, given as <see href="http://www.ietf.org/rfc/rfc2822.txt">GMT RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_completed")]
    public DateTimeOffset? DateCompleted { get; init; }

    /// <summary>
    /// The URL of this BulkHostedNumberOrder resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// The total count of phone numbers in this Bulk hosting request.
    /// </summary>
    [JsonPropertyName("total_count")]
    public int? TotalCount { get; init; } = 0;

    /// <summary>
    /// Contains a list of all the individual hosting orders and their information, for this Bulk request. Each result object is grouped by its order status. To see a complete list of order status, please check 'https://www.twilio.com/docs/phone-numbers/hosted-numbers/hosted-numbers-api/hosted-number-order-resource#status-values'.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("results")]
    public IReadOnlyList<object?>? Results { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
