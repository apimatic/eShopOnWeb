using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method we should use to call <c>conference_status_callback</c>. Can be: <c>GET</c> or <c>POST</c> and defaults to <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ConferenceStatusCallbackMethod>))]
public sealed record ConferenceStatusCallbackMethod : StringEnum<ConferenceStatusCallbackMethod>
{
    private ConferenceStatusCallbackMethod(string value) : base(value)
    {
    }

    public static readonly ConferenceStatusCallbackMethod Get = new("GET");

    public static readonly ConferenceStatusCallbackMethod Post = new("POST");

    public static ConferenceStatusCallbackMethod FromValue(string value) => FromValueCore(value);
}
