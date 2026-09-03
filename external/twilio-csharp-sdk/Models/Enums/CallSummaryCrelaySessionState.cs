using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<CallSummaryCrelaySessionState>))]
public sealed record CallSummaryCrelaySessionState : StringEnum<CallSummaryCrelaySessionState>
{
    private CallSummaryCrelaySessionState(string value) : base(value)
    {
    }

    public static readonly CallSummaryCrelaySessionState Unknown = new("unknown");

    public static readonly CallSummaryCrelaySessionState Failure = new("failure");

    public static readonly CallSummaryCrelaySessionState Ended = new("ended");

    public static readonly CallSummaryCrelaySessionState HungUp = new("hung_up");

    public static CallSummaryCrelaySessionState FromValue(string value) => FromValueCore(value);
}
