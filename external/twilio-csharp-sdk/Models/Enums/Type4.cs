using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Type of interruption event.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Type4>))]
public sealed record Type4 : StringEnum<Type4>
{
    private Type4(string value) : base(value)
    {
    }

    public static readonly Type4 Dtmf = new("DTMF");

    public static readonly Type4 Speech = new("SPEECH");

    public static Type4 FromValue(string value) => FromValueCore(value);
}
