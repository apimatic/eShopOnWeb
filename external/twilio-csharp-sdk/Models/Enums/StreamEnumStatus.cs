using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The status of the Stream. Possible values are <c>stopped</c> and <c>in-progress</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<StreamEnumStatus>))]
public sealed record StreamEnumStatus : StringEnum<StreamEnumStatus>
{
    private StreamEnumStatus(string value) : base(value)
    {
    }

    public static readonly StreamEnumStatus InProgress = new("in-progress");

    public static readonly StreamEnumStatus Stopped = new("stopped");

    public static StreamEnumStatus FromValue(string value) => FromValueCore(value);
}
