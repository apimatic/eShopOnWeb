using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<CallSummariesEnumAnsweredBy>))]
public sealed record CallSummariesEnumAnsweredBy : StringEnum<CallSummariesEnumAnsweredBy>
{
    private CallSummariesEnumAnsweredBy(string value) : base(value)
    {
    }

    public static readonly CallSummariesEnumAnsweredBy Unknown = new("unknown");

    public static readonly CallSummariesEnumAnsweredBy MachineStart = new("machine_start");

    public static readonly CallSummariesEnumAnsweredBy MachineEndBeep = new("machine_end_beep");

    public static readonly CallSummariesEnumAnsweredBy MachineEndSilence = new("machine_end_silence");

    public static readonly CallSummariesEnumAnsweredBy MachineEndOther = new("machine_end_other");

    public static readonly CallSummariesEnumAnsweredBy Human = new("human");

    public static readonly CallSummariesEnumAnsweredBy Fax = new("fax");

    public static CallSummariesEnumAnsweredBy FromValue(string value) => FromValueCore(value);
}
