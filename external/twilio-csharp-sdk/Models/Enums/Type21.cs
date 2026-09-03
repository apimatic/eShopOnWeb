using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Content type discriminator.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Type21>))]
public sealed record Type21 : StringEnum<Type21>
{
    private Type21(string value) : base(value)
    {
    }

    public static readonly Type21 Transcription = new("TRANSCRIPTION");

    public static Type21 FromValue(string value) => FromValueCore(value);
}
