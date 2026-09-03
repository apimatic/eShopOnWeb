using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// A string that shows the status of the current Bulk Hosting request, it can vary between these values: 'QUEUED','IN_PROGRESS','PROCESSED'
/// </summary>
[JsonConverter(typeof(StringEnumConverter<BulkHostedNumberOrderEnumRequestStatus>))]
public sealed record BulkHostedNumberOrderEnumRequestStatus : StringEnum<BulkHostedNumberOrderEnumRequestStatus>
{
    private BulkHostedNumberOrderEnumRequestStatus(string value) : base(value)
    {
    }

    public static readonly BulkHostedNumberOrderEnumRequestStatus Queued = new("QUEUED");

    public static readonly BulkHostedNumberOrderEnumRequestStatus InProgress = new("IN_PROGRESS");

    public static readonly BulkHostedNumberOrderEnumRequestStatus Processed = new("PROCESSED");

    public static BulkHostedNumberOrderEnumRequestStatus FromValue(string value) => FromValueCore(value);
}
