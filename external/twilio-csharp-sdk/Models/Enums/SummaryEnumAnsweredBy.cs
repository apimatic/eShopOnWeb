using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SummaryEnumAnsweredBy>))]
public sealed record SummaryEnumAnsweredBy : StringEnum<SummaryEnumAnsweredBy>
{
    private SummaryEnumAnsweredBy(string value) : base(value)
    {
    }

    public static readonly SummaryEnumAnsweredBy Unknown = new("unknown");

    public static readonly SummaryEnumAnsweredBy MachineStart = new("machine_start");

    public static readonly SummaryEnumAnsweredBy MachineEndBeep = new("machine_end_beep");

    public static readonly SummaryEnumAnsweredBy MachineEndSilence = new("machine_end_silence");

    public static readonly SummaryEnumAnsweredBy MachineEndOther = new("machine_end_other");

    public static readonly SummaryEnumAnsweredBy Human = new("human");

    public static readonly SummaryEnumAnsweredBy Fax = new("fax");

    public static SummaryEnumAnsweredBy FromValue(string value) => FromValueCore(value);
}
