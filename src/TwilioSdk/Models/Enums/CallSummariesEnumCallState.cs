using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<CallSummariesEnumCallState>))]
public sealed record CallSummariesEnumCallState : StringEnum<CallSummariesEnumCallState>
{
    private CallSummariesEnumCallState(string value) : base(value)
    {
    }

    public static readonly CallSummariesEnumCallState Ringing = new("ringing");

    public static readonly CallSummariesEnumCallState Completed = new("completed");

    public static readonly CallSummariesEnumCallState Busy = new("busy");

    public static readonly CallSummariesEnumCallState Fail = new("fail");

    public static readonly CallSummariesEnumCallState Noanswer = new("noanswer");

    public static readonly CallSummariesEnumCallState Canceled = new("canceled");

    public static readonly CallSummariesEnumCallState Answered = new("answered");

    public static readonly CallSummariesEnumCallState Undialed = new("undialed");

    public static CallSummariesEnumCallState FromValue(string value) => FromValueCore(value);
}
