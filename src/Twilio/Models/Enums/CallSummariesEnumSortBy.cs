using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<CallSummariesEnumSortBy>))]
public sealed record CallSummariesEnumSortBy : StringEnum<CallSummariesEnumSortBy>
{
    private CallSummariesEnumSortBy(string value) : base(value)
    {
    }

    public static readonly CallSummariesEnumSortBy StartTime = new("start_time");

    public static readonly CallSummariesEnumSortBy EndTime = new("end_time");

    public static CallSummariesEnumSortBy FromValue(string value) => FromValueCore(value);
}
