using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The recording's media type. Can be: <c>audio</c> or <c>video</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RecordingEnumType>))]
public sealed record RecordingEnumType : StringEnum<RecordingEnumType>
{
    private RecordingEnumType(string value) : base(value)
    {
    }

    public static readonly RecordingEnumType Audio = new("audio");

    public static readonly RecordingEnumType Video = new("video");

    public static readonly RecordingEnumType Data = new("data");

    public static RecordingEnumType FromValue(string value) => FromValueCore(value);
}
