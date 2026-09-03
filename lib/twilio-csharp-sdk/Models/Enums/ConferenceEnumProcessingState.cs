using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ConferenceEnumProcessingState>))]
public sealed record ConferenceEnumProcessingState : StringEnum<ConferenceEnumProcessingState>
{
    private ConferenceEnumProcessingState(string value) : base(value)
    {
    }

    public static readonly ConferenceEnumProcessingState Complete = new("complete");

    public static readonly ConferenceEnumProcessingState InProgress = new("in_progress");

    public static readonly ConferenceEnumProcessingState Timeout = new("timeout");

    public static ConferenceEnumProcessingState FromValue(string value) => FromValueCore(value);
}
