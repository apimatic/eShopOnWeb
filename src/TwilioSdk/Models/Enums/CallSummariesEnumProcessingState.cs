using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

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
