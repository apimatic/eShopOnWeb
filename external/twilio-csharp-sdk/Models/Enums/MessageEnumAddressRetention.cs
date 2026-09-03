using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Determines if the address can be stored or obfuscated based on privacy settings
/// </summary>
[JsonConverter(typeof(StringEnumConverter<MessageEnumAddressRetention>))]
public sealed record MessageEnumAddressRetention : StringEnum<MessageEnumAddressRetention>
{
    private MessageEnumAddressRetention(string value) : base(value)
    {
    }

    public static readonly MessageEnumAddressRetention Retain = new("retain");

    public static readonly MessageEnumAddressRetention Obfuscate = new("obfuscate");

    public static MessageEnumAddressRetention FromValue(string value) => FromValueCore(value);
}
