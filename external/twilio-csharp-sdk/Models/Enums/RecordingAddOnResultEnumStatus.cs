using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The status of the result. Can be: <c>canceled</c>, <c>completed</c>, <c>deleted</c>, <c>failed</c>, <c>in-progress</c>, <c>init</c>, <c>processing</c>, <c>queued</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RecordingAddOnResultEnumStatus>))]
public sealed record RecordingAddOnResultEnumStatus : StringEnum<RecordingAddOnResultEnumStatus>
{
    private RecordingAddOnResultEnumStatus(string value) : base(value)
    {
    }

    public static readonly RecordingAddOnResultEnumStatus Canceled = new("canceled");

    public static readonly RecordingAddOnResultEnumStatus Completed = new("completed");

    public static readonly RecordingAddOnResultEnumStatus Deleted = new("deleted");

    public static readonly RecordingAddOnResultEnumStatus Failed = new("failed");

    public static readonly RecordingAddOnResultEnumStatus InProgress = new("in-progress");

    public static readonly RecordingAddOnResultEnumStatus Init = new("init");

    public static readonly RecordingAddOnResultEnumStatus Processing = new("processing");

    public static readonly RecordingAddOnResultEnumStatus Queued = new("queued");

    public static RecordingAddOnResultEnumStatus FromValue(string value) => FromValueCore(value);
}
