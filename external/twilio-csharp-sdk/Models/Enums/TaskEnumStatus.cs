using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The current status of the Task's assignment. Can be: <c>pending</c>, <c>reserved</c>, <c>assigned</c>, <c>canceled</c>, <c>wrapping</c>, or <c>completed</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<TaskEnumStatus>))]
public sealed record TaskEnumStatus : StringEnum<TaskEnumStatus>
{
    private TaskEnumStatus(string value) : base(value)
    {
    }

    public static readonly TaskEnumStatus Pending = new("pending");

    public static readonly TaskEnumStatus Reserved = new("reserved");

    public static readonly TaskEnumStatus Assigned = new("assigned");

    public static readonly TaskEnumStatus Canceled = new("canceled");

    public static readonly TaskEnumStatus Completed = new("completed");

    public static readonly TaskEnumStatus Wrapping = new("wrapping");

    public static TaskEnumStatus FromValue(string value) => FromValueCore(value);
}
