using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The status of the Execution. Can be: <c>active</c> or <c>ended</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ExecutionEnumStatus>))]
public sealed record ExecutionEnumStatus : StringEnum<ExecutionEnumStatus>
{
    private ExecutionEnumStatus(string value) : base(value)
    {
    }

    public static readonly ExecutionEnumStatus Active = new("active");

    public static readonly ExecutionEnumStatus Ended = new("ended");

    public static ExecutionEnumStatus FromValue(string value) => FromValueCore(value);
}
