using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ConferenceEnumUpdateStatus>))]
public sealed record ConferenceEnumUpdateStatus : StringEnum<ConferenceEnumUpdateStatus>
{
    private ConferenceEnumUpdateStatus(string value) : base(value)
    {
    }

    public static readonly ConferenceEnumUpdateStatus Completed = new("completed");

    public static ConferenceEnumUpdateStatus FromValue(string value) => FromValueCore(value);
}
