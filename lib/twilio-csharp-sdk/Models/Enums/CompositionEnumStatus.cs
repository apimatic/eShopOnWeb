using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The status of the composition. Can be: <c>enqueued</c>, <c>processing</c>, <c>completed</c>, <c>deleted</c> or <c>failed</c>. <c>enqueued</c> is the initial state and indicates that the composition request has been received and is scheduled for processing; <c>processing</c> indicates the composition is being processed; <c>completed</c> indicates the composition has been completed and is available for download; <c>deleted</c> means the composition media has been deleted from the system, but its metadata is still available for 30 days; <c>failed</c> indicates the composition failed to execute the media processing task.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CompositionEnumStatus>))]
public sealed record CompositionEnumStatus : StringEnum<CompositionEnumStatus>
{
    private CompositionEnumStatus(string value) : base(value)
    {
    }

    public static readonly CompositionEnumStatus Enqueued = new("enqueued");

    public static readonly CompositionEnumStatus Processing = new("processing");

    public static readonly CompositionEnumStatus Completed = new("completed");

    public static readonly CompositionEnumStatus Deleted = new("deleted");

    public static readonly CompositionEnumStatus Failed = new("failed");

    public static CompositionEnumStatus FromValue(string value) => FromValueCore(value);
}
