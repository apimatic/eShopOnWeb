using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status - one of <c>stopped</c>, <c>in-progress</c>
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SiprecEnumStatus>))]
public sealed record SiprecEnumStatus : StringEnum<SiprecEnumStatus>
{
    private SiprecEnumStatus(string value) : base(value)
    {
    }

    public static readonly SiprecEnumStatus InProgress = new("in-progress");

    public static readonly SiprecEnumStatus Stopped = new("stopped");

    public static SiprecEnumStatus FromValue(string value) => FromValueCore(value);
}
