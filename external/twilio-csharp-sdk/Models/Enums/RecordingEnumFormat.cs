using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<RecordingEnumFormat>))]
public sealed record RecordingEnumFormat : StringEnum<RecordingEnumFormat>
{
    private RecordingEnumFormat(string value) : base(value)
    {
    }

    public static readonly RecordingEnumFormat Mka = new("mka");

    public static readonly RecordingEnumFormat Mkv = new("mkv");

    public static RecordingEnumFormat FromValue(string value) => FromValueCore(value);
}
