using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ConferenceEnumConferenceStatus>))]
public sealed record ConferenceEnumConferenceStatus : StringEnum<ConferenceEnumConferenceStatus>
{
    private ConferenceEnumConferenceStatus(string value) : base(value)
    {
    }

    public static readonly ConferenceEnumConferenceStatus InProgress = new("in_progress");

    public static readonly ConferenceEnumConferenceStatus NotStarted = new("not_started");

    public static readonly ConferenceEnumConferenceStatus Completed = new("completed");

    public static readonly ConferenceEnumConferenceStatus SummaryTimeout = new("summary_timeout");

    public static ConferenceEnumConferenceStatus FromValue(string value) => FromValueCore(value);
}
