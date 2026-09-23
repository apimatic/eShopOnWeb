using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Current status of the Action.
/// - PENDING: Action accepted, awaiting downstream confirmation
/// - COMPLETED: Downstream backend confirmed the action
/// - FAILED: Downstream backend reported a failure
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Status11>))]
public sealed record Status11 : StringEnum<Status11>
{
    private Status11(string value) : base(value)
    {
    }

    public static readonly Status11 Pending = new("PENDING");

    public static readonly Status11 Completed = new("COMPLETED");

    public static readonly Status11 Failed = new("FAILED");

    public static Status11 FromValue(string value) => FromValueCore(value);
}
