using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<CallSummariesEnumProcessingState>))]
public sealed record CallSummariesEnumProcessingState : StringEnum<CallSummariesEnumProcessingState>
{
    private CallSummariesEnumProcessingState(string value) : base(value)
    {
    }

    public static readonly CallSummariesEnumProcessingState Complete = new("complete");

    public static readonly CallSummariesEnumProcessingState Partial = new("partial");

    public static CallSummariesEnumProcessingState FromValue(string value) => FromValueCore(value);
}
