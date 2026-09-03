using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SummaryEnumProcessingState>))]
public sealed record SummaryEnumProcessingState : StringEnum<SummaryEnumProcessingState>
{
    private SummaryEnumProcessingState(string value) : base(value)
    {
    }

    public static readonly SummaryEnumProcessingState Complete = new("complete");

    public static readonly SummaryEnumProcessingState Partial = new("partial");

    public static SummaryEnumProcessingState FromValue(string value) => FromValueCore(value);
}
