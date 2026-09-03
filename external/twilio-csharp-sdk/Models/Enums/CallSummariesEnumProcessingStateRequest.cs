using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<CallSummariesEnumProcessingStateRequest>))]
public sealed record CallSummariesEnumProcessingStateRequest : StringEnum<CallSummariesEnumProcessingStateRequest>
{
    private CallSummariesEnumProcessingStateRequest(string value) : base(value)
    {
    }

    public static readonly CallSummariesEnumProcessingStateRequest Completed = new("completed");

    public static readonly CallSummariesEnumProcessingStateRequest Started = new("started");

    public static readonly CallSummariesEnumProcessingStateRequest Partial = new("partial");

    public static readonly CallSummariesEnumProcessingStateRequest All = new("all");

    public static CallSummariesEnumProcessingStateRequest FromValue(string value) => FromValueCore(value);
}
