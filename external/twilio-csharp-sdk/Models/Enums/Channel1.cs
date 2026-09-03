using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The messaging channel. Must be "WHATSAPP".
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Channel1>))]
public sealed record Channel1 : StringEnum<Channel1>
{
    private Channel1(string value) : base(value)
    {
    }

    public static readonly Channel1 Whatsapp = new("WHATSAPP");

    public static Channel1 FromValue(string value) => FromValueCore(value);
}
