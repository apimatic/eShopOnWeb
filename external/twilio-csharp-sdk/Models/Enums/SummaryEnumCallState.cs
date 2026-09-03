using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SummaryEnumCallState>))]
public sealed record SummaryEnumCallState : StringEnum<SummaryEnumCallState>
{
    private SummaryEnumCallState(string value) : base(value)
    {
    }

    public static readonly SummaryEnumCallState Ringing = new("ringing");

    public static readonly SummaryEnumCallState Completed = new("completed");

    public static readonly SummaryEnumCallState Busy = new("busy");

    public static readonly SummaryEnumCallState Fail = new("fail");

    public static readonly SummaryEnumCallState Noanswer = new("noanswer");

    public static readonly SummaryEnumCallState Canceled = new("canceled");

    public static readonly SummaryEnumCallState Answered = new("answered");

    public static readonly SummaryEnumCallState Undialed = new("undialed");

    public static SummaryEnumCallState FromValue(string value) => FromValueCore(value);
}
