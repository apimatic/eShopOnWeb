using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Shared channel identifier
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Channel>))]
public sealed record Channel : StringEnum<Channel>
{
    private Channel(string value) : base(value)
    {
    }

    public static readonly Channel Whatsapp = new("whatsapp");

    public static Channel FromValue(string value) => FromValueCore(value);
}
