using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type111>))]
public sealed record Type111 : StringEnum<Type111>
{
    private Type111(string value) : base(value)
    {
    }

    public static readonly Type111 Transcription = new("TRANSCRIPTION");

    public static Type111 FromValue(string value) => FromValueCore(value);
}
