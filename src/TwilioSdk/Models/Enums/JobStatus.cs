using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<JobStatus>))]
public sealed record JobStatus : StringEnum<JobStatus>
{
    private JobStatus(string value) : base(value)
    {
    }

    public static readonly JobStatus Created = new("CREATED");

    public static readonly JobStatus FileReceived = new("FILE_RECEIVED");

    public static readonly JobStatus Queued = new("QUEUED");

    public static readonly JobStatus InProgress = new("IN_PROGRESS");

    public static readonly JobStatus Completed = new("COMPLETED");

    public static readonly JobStatus Failed = new("FAILED");

    public static readonly JobStatus Stopped = new("STOPPED");

    public static readonly JobStatus StopRequested = new("STOP_REQUESTED");

    public static JobStatus FromValue(string value) => FromValueCore(value);
}
