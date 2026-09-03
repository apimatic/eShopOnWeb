using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The status of this conference. Can be: <c>init</c>, <c>in-progress</c>, or <c>completed</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ConferenceEnumStatus>))]
public sealed record ConferenceEnumStatus : StringEnum<ConferenceEnumStatus>
{
    private ConferenceEnumStatus(string value) : base(value)
    {
    }

    public static readonly ConferenceEnumStatus Init = new("init");

    public static readonly ConferenceEnumStatus InProgress = new("in-progress");

    public static readonly ConferenceEnumStatus Completed = new("completed");

    public static ConferenceEnumStatus FromValue(string value) => FromValueCore(value);
}
